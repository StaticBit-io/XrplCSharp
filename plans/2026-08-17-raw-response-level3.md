# Raw Response, уровень 3: развод API v1 и v2

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Убрать последнее, чем типизированная модель искажает ответ узла: подмену имён между версиями API и поля, для которых в моделях просто нет места.

**Architecture:** Уровни 0–2 сняли двойной разбор, отдали сырые байты и убрали приписки. Осталось три класса дефектов, все связанные с версиями API: имя поля подменяется при round-trip (`Amount` вместо `DeliverMax`), поля не смоделированы вовсе (`meta_blob`, `tx_blob`, `ctid`), и выбор метода молча меняет версию протокола (`Tx()` игнорирует настройку клиента).

**Tech Stack:** .NET 8/9/10, System.Text.Json, MSTest 4.0.2 (`Assert.ThrowsExactly`; `ImplicitUsings` выключен).

**Приёмка:** `dotnet build XrplCSharp.sln` (гейт CI, не отдельные проекты), unit-прогон, интеграционные на живом rippled.

---

## Что установила разведка — и что из этого следует

**Механизм алиасов v1/v2 уже существует** и сделан не этой инициативой, а релизом 10.9.1.0. Приватный set-only алиас с `[JsonInclude]`:

| Модель | v2-имя | v1-алиас | Файл |
|---|---|---|---|
| `Payment` / `PaymentResponse` | `Amount` | `DeliverMax` | `Models/Transactions/Payment.cs:59-75, 199-217` |
| `TransactionSummary` | `tx_json` | `tx` | `Models/Methods/AccountTransactions.cs:141, 156` |
| `TransactionStream` | `tx_json` | **`transaction`** | `Models/Subscriptions/TransactionStream.cs:92, 107` |

Обрати внимание на третью строку: в стриме v1-конверт называется `transaction`, а в `account_tx` — `tx`. Одно явление, два разных имени.

**Чего нет вообще:**

- `meta_blob` и `tx_blob` — `grep` по `Xrpl/` даёт ноль. Под API v2 с `binary: true` узел присылает их отдельными полями верхнего уровня, поля `meta` в ответе нет, и строковая ветка `MetaBinaryConverter.Read` (`Converters/MetaBinaryConverter.cs:35-53`) не срабатывает **никогда**. Замер: `TransactionSummary` теряет ответ целиком, 2246 B → 195 B.
- `ctid` в ответе — есть только `TxRequest.CtId` (`Models/Methods/Tx.cs:28`), на стороне ответа нигде.
- `close_time_iso` в `TransactionResponse`/`BaseTransactionResponse` — есть в `TransactionSummary`, `TransactionStream`, `LOLedger`, но не в v1-модели транзакции.
- `status` в `XrplResponse<T>` — `BaseResponse.Status` существует, но `XrplResponse.From<T>` его не переносит (`Client/XrplResponse.cs:151-162`). Через `Raw` недостижим: `Raw` — срез `result`, а `status` вне его.

**`Tx()` жёстко ставит `api_version = 1`** (`Client/IXrplClient.cs:889`), игнорируя `ClientOptions.ApiVersion`, который по умолчанию равен 2. Внутри SDK `Tx()` не вызывается ни разу — единственный внутренний вызов это `TxV2` в `Sugar/Submit.cs:419`.

**Уже закрыто уровнем 2, перепроверено прогоном:** `DeletedNode.FinalFields.Flags` больше не теряется. Спека фиксировала этот дефект на состоянии до nullability. Остаточный канал потери — `LedgerEntryType`, не входящий в switch `GetTypeForLedgerEntry` (`Converters/LedgerObjectConverter.cs:176-210`): такой объект падает в голый `BaseLedgerEntry`, у которого нет ничего, кроме трёх свойств. Это общий механизм, не специфичный для `DeletedNode`.

---

## Решения, принятые для этого уровня

**1. `Amount`/`DeliverMax` — запоминаем, какое имя пришло.** Сейчас модель хранит одно поле и при обратной сериализации всегда пишет `Amount`. Спека справедливо называет это худшим случаем: не потеря, а **подмена на другое валидное имя протокола**, которую подпись «(reconstructed)» не покрывает. Чиним: модель запоминает, под каким именем поле пришло, и пишет обратно его же. Цена — одно приватное поле и условная сериализация.

**2. `Tx()` переименовывается в `TxV1()`.** Метод не станет уважать `ClientOptions.ApiVersion`: он привязан к v1-модели `TransactionResponse`, и отдать в неё v2-ответ значит потерять `tx_json` целиком. Но имя обязано говорить правду. `Tx` → `TxV1`, рядом остаётся `TxV2`; молчаливого расхождения с настройкой клиента больше нет, потому что версия названа в имени метода. Политика мажора позволяет — переходных мостиков не заводим.

**3. `meta_blob`, `tx_blob`, `ctid`, `close_time_iso`, `status` — просто добавляем.** Чистые пробелы, спорить не о чем.

**4. Сырое для стримов — не в этот уровень.** `TransactionStream` не имеет `Raw`, потому что стрим-сообщения идут через `EnqueueStreamMessage(Text())` (`connection.cs:3267, 3302`) — там уже UTF-16 строка, кадра нет. Дать стримам ту же точность значит переписать путь стримов на байты, а это по объёму сопоставимо с уровнем 0. Выносится отдельным пунктом в «не делает», с обоснованием.

---

## Breaking — поимённо, без мостиков

| Член | Судьба |
|---|---|
| `IXrplClient.Tx(TxRequest, CancellationToken)` | **переименован** в `TxV1` |
| `MetaBinaryConverter` | остаётся, но перестаёт быть единственным путём для binary — см. Task 2 |

---

### Task 1: Пробелы, которых просто нет

Самое дешёвое и бесспорное — делаем первым.

**Files:**
- Modify: `Xrpl/Models/Transactions/BaseTransactionResponse.cs` — `close_time_iso`, `ctid`
- Modify: `Xrpl/Models/Methods/AccountTransactions.cs` — `ctid` в `TransactionSummary`
- Modify: `Xrpl/Client/XrplResponse.cs` — `Status`
- Test: `Tests/Xrpl.Tests/Models/`

- [ ] **Step 1: Тесты на живых данных**

В `C:\Users\Evril\AppData\Local\Temp\claude\...\scratchpad` лежат реальные ответы mainnet (`tx_raw.json`, `account_tx_raw.json`). Возьми из них фактические значения `close_time_iso` и `ctid` и напиши тесты: десериализация ответа сохраняет оба поля, round-trip их не теряет.

- [ ] **Step 2: Добавить свойства**

`close_time_iso` — тип `DateTime?` с `[JsonConverter(typeof(FromStringDateTimeConverter))]`, как это уже сделано в `TransactionSummary` (`AccountTransactions.cs:103-105`). Скопируй форму оттуда, не изобретай.

`ctid` — `string?`, `[JsonPropertyName("ctid")]`.

`Status` в `XrplResponse<T>` — новый член плюс параметр конструктора; `XrplResponse.From<T>` переносит `envelope?.Status`. Порядок параметров конструктора выбери так, чтобы `Status` встал рядом с прочими членами конверта, и **поправь все существующие вызовы конструктора**, включая тесты.

- [ ] **Step 3: Приёмка и коммит**

```bash
dotnet build XrplCSharp.sln -v q --nologo
```
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestU"
```
```bash
git commit -m "feat(models): close_time_iso, ctid и status больше не теряются"
```

---

### Task 2: `meta_blob` и `tx_blob` — binary-режим API v2

**Files:**
- Modify: `Xrpl/Models/Methods/AccountTransactions.cs` (`TransactionSummary`)
- Modify: `Xrpl/Models/Transactions/BaseTransactionResponse.cs`

- [ ] **Step 1: Тест на реальном ответе**

В scratchpad есть `tx_binary_raw.json` — реальный ответ mainnet при `binary: true, api_version: 2`. Его форма:

```json
{"result":{"close_time_iso":"...","ctid":"...","hash":"...","ledger_hash":"...",
           "ledger_index":348734,"meta_blob":"201C...","status":"success",
           "tx_blob":"1200002200...","validated":true}}
```

Напиши тест: десериализация этого ответа сохраняет `meta_blob` и `tx_blob`. Сейчас он упадёт — 2246 B схлопываются в 195 B.

- [ ] **Step 2: Добавить свойства**

`string? MetaBlob` с `[JsonPropertyName("meta_blob")]` и `string? TxBlob` с `[JsonPropertyName("tx_blob")]`.

**Не трогай `MetaBinaryConverter`.** Его строковая ветка обслуживает v1 (`"meta": "<hex>"`) и работает корректно; v2 присылает другие поля, и им нужны свои свойства. Задокументируй это в XML-doc обоих новых членов: под какой версией и каким флагом они приходят.

- [ ] **Step 3: Приёмка и коммит**

```bash
git commit -m "feat(models): meta_blob и tx_blob API v2 больше не теряются"
```

---

### Task 3: `Amount` / `DeliverMax` — модель перестаёт подменять имя

Самая содержательная часть уровня.

**Files:**
- Modify: `Xrpl/Models/Transactions/Payment.cs`
- Test: `Tests/Xrpl.Tests/Models/`

- [ ] **Step 1: Тест, фиксирующий подмену**

На реальном фрагменте: ответ v2 несёт `"DeliverMax": {...}` и **не** несёт `Amount`. Тест: после round-trip выходной JSON содержит `DeliverMax` и не содержит `Amount`. Сейчас упадёт — сериализуется `Amount`.

Второй тест, симметричный: ответ v1 несёт `Amount`, round-trip даёт `Amount` и не даёт `DeliverMax`.

- [ ] **Step 2: Запомнить имя, под которым пришло**

Сейчас (`Payment.cs:59-75`, `199-217`):

```csharp
[JsonConverter(typeof(CurrencyConverter))]
public Currency Amount { get; set; }

[JsonInclude]
[JsonPropertyName("DeliverMax")]
[JsonConverter(typeof(CurrencyConverter))]
private Currency? DeliverMax
{
    set => Amount = value;
}
```

Добавь приватное поле, отмечающее, что значение пришло под именем `DeliverMax`, и сделай сериализацию условной: выводить `Amount`, когда пришло `Amount` или объект собран кодом; выводить `DeliverMax`, когда пришло оно.

Условная сериализация в System.Text.Json делается через `ShouldSerialize`-подобный приём: свойство остаётся, но помечается `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`, а рядом заводится второе, зеркальное. Выбери реализацию по месту — важно поведение, а не приём. Если окажется, что чисто атрибутами это не выражается, напиши маленький `JsonConverter` на `Payment`/`PaymentResponse`, но тогда **проверь, что он не ломает** существующий `TransactionResponseConverter`, который выбирает подтип.

**Свойство `Amount` остаётся публичным и единственным для чтения** — потребитель не должен гадать, откуда брать сумму. Меняется только то, под каким именем она уходит обратно.

- [ ] **Step 3: Проверить на живых данных**

Прогон по `account_tx_raw.json`: было 4 приписанных `tx_json.Amount` и 4 потерянных `tx_json.DeliverMax`. Должно стать 0 и 0.

- [ ] **Step 4: Приёмка и коммит**

```bash
git commit -m "fix(models)!: Payment не подменяет DeliverMax на Amount при обратной сериализации"
```

---

### Task 4: `Tx` → `TxV1`

**Files:**
- Modify: `Xrpl/Client/IXrplClient.cs:271, 887-891`
- Modify: потребители — по списку компилятора

- [ ] **Step 1: Переименовать**

`Tx` → `TxV1` в интерфейсе и реализации. `request.ApiVersion = 1` остаётся — теперь это соответствует имени.

XML-doc обоих методов должен объяснять разницу: `TxV1` отдаёт `TransactionResponse` с полями транзакции на верхнем уровне; `TxV2` отдаёт `TransactionSummary`, где `tx_json` и `meta` — соседи, как их присылает v2. И что выбор метода задаёт версию протокола независимо от `ClientOptions.ApiVersion`.

- [ ] **Step 2: Починить потребителей**

`grep` покажет. Внутри SDK вызовов `Tx()` нет — только тесты и, возможно, демо-проекты.

- [ ] **Step 3: Приёмка и коммит**

```bash
dotnet build XrplCSharp.sln -v q --nologo
```
```bash
git commit -m "refactor(client)!: Tx переименован в TxV1 — версия протокола названа в имени"
```

---

### Task 5: `CHANGES.md` и финальная приёмка уровня

- [ ] **Step 1: Раздел**

Назвать: переименование `Tx` → `TxV1` с объяснением почему; новые поля; исправление подмены `DeliverMax`. Показать код «было → стало» для переименования.

- [ ] **Step 2: Полная приёмка**

```bash
dotnet build XrplCSharp.sln -v q --nologo
```
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestU"
```
```bash
docker compose -f .ci-config/docker-compose.ci.yml up -d
```
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestI"
```
```bash
docker compose -f .ci-config/docker-compose.ci.yml down
```

- [ ] **Step 3: Замер на живых данных**

Прогон диффа по всем файлам в scratchpad. Ожидается: приписанных 0 везде; потерянных — только то, что осознанно осталось.

---

## Что этот уровень сознательно не делает

- **Не даёт стримам сырой текст.** `TransactionStream` идёт через `EnqueueStreamMessage(Text())` — там уже UTF-16 строка, кадра нет. Перевести стримы на байты значит повторить уровень 0 для второго пути; объём несопоставим с остальным содержимым уровня. Остаётся как названный остаток.
- **Не чинит потерю полей у нераспознанного `LedgerEntryType`.** Объект, чей тип не входит в switch `GetTypeForLedgerEntry`, десериализуется в голый `BaseLedgerEntry` и теряет всё, кроме трёх свойств. Это ровно тот сценарий, который закрывает `[JsonExtensionData]` из уровня 2 — если та задача завершится исходом A, вопрос снимается; если нет, он остаётся открытым и его место в уровне 4.
- **Не трогает `TransactionResponse.TransactionType`**, оставленный non-nullable в уровне 2: это дискриминатор на пути подписи.
