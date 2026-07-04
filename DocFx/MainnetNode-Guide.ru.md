# Гайд: Mainnet-нода

Как поднять собственную ноду `rippled`, подключённую к **mainnet** XRP Ledger: установка, конфигурация, работа под systemd, мониторинг и — критично — своевременное обновление, чтобы нода не стала amendment-blocked.

Про локальную ноду для разработки — [гайд по standalone-ноде](StandaloneNode-Guide.ru.md).

## Зачем своя нода

- Приложения ходят на `ws://localhost` — без rate-limit'ов, без доверия третьей стороне, минимальная задержка для `submit` и подписок
- Полный контроль над глубиной истории и нагрузкой на API
- Публичные кластеры (`wss://xrplcluster.com`, `wss://s1.ripple.com`) душат тяжёлых пользователей и могут отставать в нагруженные дни

Роли нод (гайд покрывает первую):

| Роль | Назначение |
|------|-----------|
| Stock-нода | Отслеживает сеть, отдаёт API, сабмитит транзакции — то, что нужно приложению |
| Full-history нода | То же + полная история с леджера 32570; десятки ТБ NVMe |
| Валидатор | Участвует в консенсусе; отдельная церемония ключей и hardening — см. [доку xrpl.org](https://xrpl.org/run-rippled-as-a-validator.html) |

## Железо (stock-нода, 2026)

| Ресурс | Минимум | Рекомендуется |
|--------|---------|---------------|
| CPU | 4 ядра / 8 потоков, высокая частота | 8+ физических ядер |
| RAM | 32 ГБ | 64 ГБ |
| Диск | NVMe SSD, 10k+ устойчивых IOPS. **Никаких HDD и сетевых хранилищ** | NVMe RAID |
| Объём | ~50 ГБ на несколько дней истории | 300+ ГБ на ~месяц (см. `online_delete`) |
| Сеть | 100 Мбит, стабильная | 1 Гбит |

Расход диска определяется исключительно глубиной хранения: mainnet закрывает леджер каждые 3–5 секунд круглосуточно.

## Установка (Ubuntu / Debian)

Пакеты публикуются в официальном apt-репозитории (канал `stable`):

```bash
# ключ репозитория
sudo install -m 0755 -d /usr/share/keyrings
curl -fsSL https://repos.ripple.com/repos/api/gpg/key/public | \
    sudo gpg --dearmor -o /usr/share/keyrings/ripple-key.gpg

# репозиторий — codename подставляется из вашего релиза ОС автоматически
echo "deb [signed-by=/usr/share/keyrings/ripple-key.gpg] https://repos.ripple.com/repos/rippled-deb $(lsb_release -cs) stable" | \
    sudo tee /etc/apt/sources.list.d/ripple.list

sudo apt update && sudo apt install -y rippled
```

Пакет устанавливает:

| Путь | Назначение |
|------|-----------|
| `/opt/ripple/bin/rippled` | бинарник |
| `/etc/opt/ripple/rippled.cfg` | основной конфиг |
| `/etc/opt/ripple/validators.txt` | источник UNL (списка доверенных валидаторов) |
| `/lib/systemd/system/rippled.service` | systemd-юнит |
| `/var/lib/rippled/` | базы данных (сюда монтируйте NVMe-том) |

## Конфигурация

Правится `/etc/opt/ripple/rippled.cfg`. Продакшен-скелет для ноды под приложение:

```ini
[server]
port_rpc_admin_local
port_ws_admin_local
port_ws_public
port_peer

# admin — ТОЛЬКО localhost, наружу не открывать никогда
[port_rpc_admin_local]
port = 5005
ip = 127.0.0.1
admin = 127.0.0.1
protocol = http

[port_ws_admin_local]
port = 6006
ip = 127.0.0.1
admin = 127.0.0.1
protocol = ws

# публичный WebSocket для ваших приложений (приватный интерфейс,
# либо 127.0.0.1 + nginx с TLS спереди)
[port_ws_public]
port = 6005
ip = 0.0.0.0
protocol = ws
# лимит ресурсов на публичных клиентов:
# send_queue_limit = 500

# peer-протокол — вот его открываем в интернет
[port_peer]
port = 51235
ip = 0.0.0.0
protocol = peer

[node_size]
huge                     # medium при 32 ГБ RAM, huge при 64 ГБ+

[node_db]
type = NuDB              # NuDB для продакшен-нод (append-only, дружелюбен к SSD)
path = /var/lib/rippled/db/nudb
online_delete = 512000   # хранить ~512k леджеров (~3-4 недели); минимум 256
advisory_delete = 0

[database_path]
/var/lib/rippled/db

[ledger_history]
256                      # сколько леджеров догружать при старте; <= online_delete

[debug_logfile]
/var/log/rippled/debug.log

[sntp_servers]
time.windows.com
time.apple.com
time.nist.gov
pool.ntp.org

[validators_file]
validators.txt

[ssl_verify]
1
```

Ключевые моменты:

- **`network_id` для mainnet не задаётся** (он и есть значение по умолчанию). Ошибочно выставленный `network_id` — классический способ оказаться не в той сети.
- **UNL**: штатный `validators.txt` указывает на dUNL-паблишеров (`vl.ripple.com`, `unl.xrplf.org`). Не редактируйте его без чёткого понимания зачем.
- **`online_delete`** — то, что делает диск конечным: нода держит скользящее окно леджеров и удаляет старые. Диск считается под окно, а не наоборот.
- **Admin-порты наружу не открывать.** `admin = 127.0.0.1`; если нужен удалённый API — публикуйте только `port_ws_public` за nginx с TLS (паттерн reverse-proxy с WebSocket upgrade-заголовками).
- **Firewall**: входящий `51235/tcp` открыт миру (peer-протоколу полезны входящие соединения), остальное закрыто или внутреннее.

## Работа под systemd

```bash
sudo systemctl enable --now rippled
sudo systemctl status rippled

# логи
journalctl -u rippled -f
```

Первый старт на mainnet: нода получает последний validated-леджер и догружает `ledger_history`. До `"server_state": "full"` — обычно **10–30 минут** (на скромном железе дольше).

### Проверка здоровья

```bash
/opt/ripple/bin/rippled server_info | jq '.result.info | {build_version, server_state, complete_ledgers, peers, load_factor, amendment_blocked}'
```

| Поле | Здоровое значение |
|------|-------------------|
| `server_state` | `full` (у валидатора — `proposing`) |
| `complete_ledgers` | непрерывный диапазон, например `95000000-95120000`, не `empty` |
| `peers` | 10+ |
| `load_factor` | 1 (всплески при fee escalation — норма) |
| `amendment_blocked` | **должно отсутствовать** — см. ниже |

## Обновление — и почему это не опционально

XRPL развивается через **амендменты**. Через две недели после того, как амендмент набирает >80% голосов валидаторов, он активируется во всей сети. Нода, чей бинарник не знает активированный амендмент, становится **amendment-blocked**: перестаёт отслеживать сеть, отказывается сабмитить транзакции, а в `server_info` появляется `"amendment_blocked": true`. Лечится только обновлением.

Практическая политика:

- Подпишитесь на [релизы rippled](https://github.com/XRPLF/rippled/releases) и [блог XRPL](https://xrpl.org/blog/); обновляйтесь в течение дней после релиза, а не месяцев
- Дашборд голосования ([xrpscan.com/amendments](https://xrpscan.com/amendments) или команда `feature`) показывает очередь — всё, что выше 80%, это ваш таймер дедлайна

Процедура обновления (apt):

```bash
sudo apt update
sudo apt install --only-upgrade rippled
sudo systemctl restart rippled
watch -n 5 "/opt/ripple/bin/rippled server_info | jq -r '.result.info.server_state'"
```

Даунтайм — минуты: после рестарта нода быстро ресинкается к текущему леджеру (историю не переигрывает). Конфиги апгрейд не перезаписывает; после мажорных версий сверьтесь с поставляемым примером (`/etc/opt/ripple/rippled.cfg.dpkg-dist`, если есть).

Чтобы застраховаться от неожиданных мажорных апгрейдов — `apt-mark hold rippled` и обновление вручную.

## Подключение XrplCSharp

```csharp
using Xrpl.Client;

// «голый» ws:// допустим ТОЛЬКО внутри доверенной приватной сети / localhost
IXrplClient client = new XrplClient("ws://10.0.0.5:6005");
// всё, что доступно снаружи, — только через TLS (nginx перед port_ws_public):
// IXrplClient client = new XrplClient("wss://xrpl-node.example.com");
await client.Connect();

ServerInfo info = await client.ServerInfo();
Console.WriteLine(info.Info.CompleteLedgers);
```

Рекомендации для продакшена с этим SDK:

- Клиент — на **публичный** WS-порт своей ноды; admin-порты только для эксплуатации
- Проверяйте, что `complete_ledgers` покрывает запрашиваемый диапазон — `account_tx` за пределами окна хранения вернёт неполные данные; за глубокой историей — full-history провайдер или [Clio](https://github.com/XRPLF/clio)
- `SubmitAndWait` опирается на `LastLedgerSequence` — его гарантии реальны только на здоровой синхронизированной ноде

## Диагностика

| Симптом | Причина | Решение |
|---|---|---|
| `"amendment_blocked": true` | Бинарник старее активированного амендмента | Немедленно обновить rippled |
| `server_state` застрял в `connected`/`syncing` | Слабый диск (IOPS), кривые часы, мало пиров | Только NVMe; проверить NTP (`[sntp_servers]`, `timedatectl`); открыть входящий 51235 |
| `complete_ledgers: empty` при долгом аптайме | Нода постоянно теряет синк, история сбрасывается | То же — почти всегда IOPS диска или нехватка RAM |
| Диск растёт бесконечно | Не задан `online_delete` | Задать `online_delete` в `[node_db]` и перезапустить |
| `noCurrent` / `noNetwork` из API | Нода ещё не синхронизировалась или потеряла кворум | Дождаться `full`; проверить пиров и часы |
| Высокий `load_factor` на ваших запросах | Публичный порт перегружен или клиенты злоупотребляют | Rate-limit на nginx; масштабировать `node_size`/железо |
| Crash-loop после правки конфига | Синтаксическая ошибка или неизвестная секция для этой версии | `journalctl -u rippled -n 50`; сверить с поставляемым примером конфига |
