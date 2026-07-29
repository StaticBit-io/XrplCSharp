# Гайд: Standalone-нода

Как поднять локальную ноду `rippled`/`xrpld` в standalone-режиме для разработки и интеграционных тестов — и почему свежезапущенная нода отвечает `temDISABLED` почти на всё, пока не активированы амендменты.

Про продакшен-ноду в боевой сети — [гайд по mainnet-ноде](MainnetNode-Guide.ru.md).

> **Кратко**: одного `rippled -a --start` **недостаточно**. «Голая» standalone-нода стартует (почти) без амендментов, поэтому `AMMCreate`, `NFTokenMint`, `MPTokenIssuanceCreate` и большинство современных типов транзакций падают с `temDISABLED`. Используйте готовые Docker-стенды из `.ci-config/` этого репозитория либо настройте активацию амендментов самостоятельно, как описано ниже.

## Быстрый старт (рекомендуется)

В репозитории есть два готовых Docker Compose стенда. Оба включают sidecar `ledger-acceptor`, закрывающий леджер каждые 4 секунды.

### Стенд 1: релизная нода (rippled 3.2.0) — используется CI

```bash
docker compose -f .ci-config/docker-compose.ci.yml up -d

# запуск интеграционных тестов
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestI"

docker compose -f .ci-config/docker-compose.ci.yml down
```

### Стенд 2: nightly develop нода — для невыпущенных амендментов

Амендменты, существующие только в ветке `develop` rippled (например, `BatchV1_1`, `PermissionDelegationV1_1`), отсутствуют в релизных образах. Второй стенд собирает запиненный nightly `xrpld` и включает их в genesis:

```bash
# оба стенда используют одни порты — сначала остановите первый
docker compose -f .ci-config/docker-compose.ci.yml down

docker compose -f .ci-config/docker-compose.batchv11.yml up -d --build
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestIBatch|TestIDelegateSet"
docker compose -f .ci-config/docker-compose.batchv11.yml down
```

### Порты (оба стенда)

| Порт | Назначение |
|------|-----------|
| 5005 | JSON-RPC (admin) |
| 5006 | JSON-RPC (admin, используется ledger-acceptor) |
| 6006 | WebSocket (admin) — интеграционные тесты подключаются сюда (`ws://localhost:6006`) |
| 6007 | WebSocket с `admin_user`/`admin_password` — только для `TestIAdminCredentials` |

Порт 6007 (`[port_ws_admin_auth]`) существует, чтобы проверить, что admin-команды по WS открываются только при заданных `ClientOptions.AdminUser`/`AdminPassword`. rippled передаёт эти креды **внутри JSON** запроса — Basic-заголовок на ws-рукопожатии сама нода не проверяет (её `user`/`password` относятся только к HTTP JSON-RPC). Остальные тесты порт не используют.

## Почему возникает `temDISABLED`

Каждая функция, закрытая [амендментом](https://xrpl.org/amendments.html) (AMM, NFT, MPT, Batch, …), на уровне транзакции проверяет, **включён ли амендмент в леджере**. В mainnet амендменты включаются голосованием валидаторов; в standalone-режиме **голосования нет** — нет валидаторов, нет majority, само по себе ничего не активируется.

«Голый» `rippled -a --start` создаёт genesis-леджер, где активны только амендменты с `VoteBehavior::DefaultYes` (несколько fix-ов). Всё остальное неактивно, и нода отвечает:

```json
{ "engine_result": "temDISABLED", "engine_result_message": "The transaction requires logic that is currently disabled." }
```

Это не ошибка в вашей транзакции — на ноде просто не включена нужная функция.

### Как проверить, что активно

```bash
# статус одного амендмента (admin RPC)
curl -s -X POST http://localhost:5005/ -d '{"method":"feature","params":[{"feature":"AMM"}]}'
# => { "...": { "enabled": true|false, "supported": true, ... } }

# полный список включённых амендментов прямо из леджера
curl -s -X POST http://localhost:5005/ -d '{"method":"ledger_entry","params":[{"index":"7DB0788C020F02780A673DC74757F23823FA3014C1866E72CC4CD8B226CD6EF4","ledger_index":"validated"}]}'
```

`7DB0788C…D6EF4` — well-known индекс леджер-объекта `Amendments`. Если id вашего амендмента нет в его массиве `Amendments` — будет `temDISABLED`.

Id амендмента — это `sha512half` от его имени:

```python
import hashlib
hashlib.sha512(b"AMM").digest()[:32].hex().upper()
# 8CC0774A3BF66D1D22E76BBDA8E8A232E6B6313834301B3B23E8601196AE6455
```

## Активация амендментов в genesis

Секция конфига **зависит от версии rippled**:

### rippled ≤ 3.2.x (релизные образы): секция `[features]`

Просто имена амендментов, по одному в строке. Полный рабочий набор под 3.2.0 — в `.ci-config/rippled.cfg`:

```ini
[features]
AMM
Clawback
MPTokensV1
NFTokenMintOffer
...
```

При старте `rippled -a --start` нода включит всё перечисленное в genesis.

**Грабли:**
- **Неизвестное имя** (опечатка или амендмент, которого нет в этом бинарнике) роняет ноду на старте: `Unknown feature: X in config file`.
- **Retired-амендменты** (до-2.0: `MultiSign`, `Escrow`, `DepositAuth`, `fix1368`, …) навсегда вшиты в протокол, и их **нельзя перечислять** — новые бинарники отвергают их как неизвестные.

### rippled `develop` / 3.3.x (nightly `xrpld`): секция `[amendments]`

В ветке `develop` секция `[features]` **больше не регистрирует genesis-голоса**. Genesis-амендменты берутся из секции `[amendments]` со строками вида `<хеш> <имя>`:

```ini
[amendments]
8CC0774A3BF66D1D22E76BBDA8E8A232E6B6313834301B3B23E8601196AE6455 AMM
9F287AED3CDB50A7BD1ACEC24296A30C9B5230CCD136219317AC790E3B884377 BatchV1_1
...
```

Полный пример — `.ci-config/rippled.batchv11.cfg`. Учтите: admin-команда `feature` тоже **не может** активировать амендмент на работающей standalone-ноде — при `VoteBehavior::DefaultNo` снятие вето не означает голос «за», да и голосования в standalone нет вовсе. **Секция `[amendments]` при `--start` — единственный способ.**

### Специфика nightly-образа

- В `develop` rippled переименован в **`xrpld`**; nightly apt-канал на `repos.ripple.com` публикует пакет `xrpld`.
- **Пинуйте версию** (`ARG XRPLD_VERSION` в `.ci-config/Dockerfile.nightly`): в середине 2026 формат таймстампа сборки сократился с 14 до 12 цифр, из-за чего Debian-сортировка версий ставит старые сборки выше новых, и непинованный `apt install xrpld` тянет устаревший билд.

## Продвижение леджера

В standalone-режиме леджеры сами не закрываются. Любая транзакция висит в открытом леджере, пока не вызвать:

```bash
curl -s -X POST http://localhost:5006/ -d '{"method":"ledger_accept"}'
```

Compose-стенды включают sidecar `ledger-acceptor`, который делает это каждые 4 секунды, поэтому `SubmitAndWait` работает из коробки. Если поднимать ноду вручную и забыть про это — транзакции навсегда останутся в `terQUEUED`/pending.

## Фондирование аккаунтов

Faucet-а в standalone нет. Все 100 миллиардов XRP лежат на genesis-аккаунте:

| | |
|---|---|
| Адрес | `rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh` |
| Seed | `snoPBrXtMeMyMHUVTgbuqAfg1SUTb` (well-known seed от `masterpassphrase`) |

Тестовые кошельки фондируются обычным `Payment` с него — референсная реализация с ретраями: `Tests/Xrpl.Tests/Integration/Utils.cs` (`FundAccount`).

```csharp
XrplWallet master = XrplWallet.FromSeed("snoPBrXtMeMyMHUVTgbuqAfg1SUTb", null, "secp256k1");
```

Обратите внимание: ключ genesis-аккаунта — **secp256k1**, не Ed25519.

## Диагностика

| Симптом | Причина | Решение |
|---|---|---|
| `temDISABLED` на AMM/NFT/MPT/Batch/… | Амендмент не включён на ноде | Активировать в genesis: `[features]` (≤3.2.x) или `[amendments]` (develop); использовать стенды из `.ci-config` |
| Нода падает: `Unknown feature: X in config file` | Опечатка, амендмента нет в бинарнике, либо перечислен retired-амендмент | Исправить имя; выровнять список под версию бинарника; убрать retired |
| Нода падает: `Invalid entry 'X' in [amendments]` | Строки `[amendments]` — это `<хеш> <имя>`, а не просто имена | Добавить sha512half-хеш перед каждым именем |
| `actNotFound` / `terNO_ACCOUNT` | Тестовый аккаунт не фондирован | Сначала Payment с genesis-аккаунта |
| Транзакция зависла, `SubmitAndWait` по таймауту | Никто не вызывает `ledger_accept` | Запустить ledger-acceptor (входит в compose-стенды) |
| `feature` RPC показывает `"vetoed": false`, но `"enabled": false` навсегда | Снятие вето — не голос «за»; голосования в standalone нет вовсе | Амендменты активируются только через genesis-секции конфига |
| Амендмент-зависимые тесты скипаются (inconclusive) | `AmendmentGuard` увидел, что амендмент неактивен на ноде | Ожидаемо на релизном стенде; для этих тестов нужен nightly-стенд |

## Как это устроено в интеграционных тестах SDK

Амендмент-зависимые тест-классы (`TestIBatch`, `TestIDelegateSet`) используют `Tests/Xrpl.Tests/Integration/AmendmentGuard.cs`: `ClassInitialize` читает леджер-объект `Amendments` и помечает класс inconclusive, если нужный амендмент неактивен. Так CI на релизном образе остаётся зелёным, а те же тесты реально выполняются на nightly-стенде. Чтобы загейтить новый класс — добавьте константу с id амендмента в `AmendmentGuard` и вызовите `Assert.Inconclusive` из `TestInitialize` при неактивности.
