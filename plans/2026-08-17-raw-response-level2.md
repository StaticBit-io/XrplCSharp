# Raw Response, уровень 2: модель перестаёт врать

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Убрать из типизированной модели значения, которых узел не присылал, и перестать молча терять поля, которых модель не знает.

**Architecture:** Два независимых дефекта, лечатся по-разному. Приписанные нули — от non-nullable CLR-свойств: лечится nullability, а объём определяется не на глаз, а по vendored `ledger_entries.macro` из rippled, который уже лежит в репозитории вместе с парсером требуемости. Молча теряемые поля — отсутствием места, куда их положить: лечится `[JsonExtensionData]`. Плюс известный остаток уровня 0 — `BaseResponse.Id` и `ErrorResponse.Request` всё ещё `object`.

**Tech Stack:** .NET 8/9/10, System.Text.Json, MSTest 4.0.2 (`Assert.ThrowsExactly`; `ImplicitUsings` выключен).

**Базовая линия:** 1018 unit-тестов и 265 интеграционных, 0 падений. `dotnet build XrplCSharp.sln` — 0 ошибок (это гейт CI, приёмка проверяет именно его, а не отдельные проекты).

---

## Замеры, на которых стоит план

Сняты разведочным прогоном по `RippledLedgerEntryFormats.Parse()` и рефлексии над моделями:

| Что | Число |
|---|---|
| Опциональных/дефолтных полей ledger-объектов в протоколе | 160 |
| Из них модель **не может выразить отсутствие** — прямое нарушение | **9** |
| Всего свойств в ledger-моделях | 449 |
| Из них non-nullable value-типа | **80** |
| Non-nullable value-свойств в `Models/Transactions` | 51 |

**Девять прямых нарушений** (поле объявлено `Optional`/`Default`, свойство не может быть пустым):

```
AMM.TradingFee : UInt32 (Default)
FeeSettings.ReferenceFeeUnits : UInt32 (Optional)
FeeSettings.ReserveBase : UInt32 (Optional)
FeeSettings.ReserveIncrement : UInt32 (Optional)
LedgerHashes.FirstLedgerSequence : UInt32 (Optional)
LedgerHashes.LastLedgerSequence : UInt32 (Optional)
MPTokenIssuance.AssetScale : Byte (Default)
PayChannel.SourceTag : UInt32 (Optional)
PayChannel.DestinationTag : UInt32 (Optional)
```

## Почему nullable нужна не только этим девяти

Требуемость поля в протоколе описывает **сам ledger-объект**. Но те же модели переиспользуются как содержимое `PreviousFields`, `FinalFields` и `NewFields`, а это **частичные проекции**: `PreviousFields` по протоколу несёт только изменившиеся члены. Там отсутствовать может и обязательное поле — именно так `Flags`, `OwnerCount`, `Sequence`, `PreviousTxnLgrSeq` и `LedgerEntryType` появляются в реконструкции нулями, которых узел не присылал (156 приписанных членов на десяти записях `account_tx`, см. спеку §2).

Отсюда правило уровня: **любое value-свойство модели, которая может оказаться частичной проекцией, обязано быть nullable.** Это все 80 в ledger-моделях и 51 в транзакциях. Разделять «эти nullable, эти нет» — значит принимать 131 ручное решение и удерживать их согласованными вручную; тест соответствия дешевле и не забывает.

---

## File Structure

**Создаются:**

- `Tests/Xrpl.Tests/Models/TestUNullabilityConformance.cs` — класс `TestUNullabilityConformance`. Две проверки: свойство под `Optional`/`Default` полем обязано быть nullable (авторитетно, по macro); любое value-свойство ledger-модели обязано быть nullable (правило частичной проекции). Пишется **первым** и сначала краснеет.

**Изменяются:**

- `Xrpl/Models/Ledger/*.cs` — 80 свойств.
- `Xrpl/Models/Transactions/*.cs` — 51 свойство, включая `NodeBase.LedgerEntryType`.
- `Xrpl/Models/Subscriptions/BaseResponse.cs`, `ErrorResponse.cs` — `Id` и `Request` уходят с `object`.
- `Xrpl/Sugar/*`, `Xrpl/Wallet/*` — места, где `uint` станет `uint?`.
- Тесты, демо-проекты — по списку компилятора.
- `CHANGES.md`.

**Breaking — поимённо, без мостиков** (политика мажора, см. спеку):

| Член | Судьба |
|---|---|
| ~131 value-свойство моделей | `T` → `T?` |
| `BaseResponse.Id` (`object?`) | → строго типизированное, см. Task 4 |
| `ErrorResponse.Request` (`object`) | → `JsonSlice` + `RawRequest`, как `result` |

---

### Task 1: Тест соответствия nullability

Пишется первым: он определяет объём и защищает от регресса.

**Files:**
- Create: `Tests/Xrpl.Tests/Models/TestUNullabilityConformance.cs`

- [ ] **Step 1: Написать тест**

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Holds the models to what the protocol says can be absent. A non-nullable CLR property cannot
    /// express absence, so it re-serializes as a zero the node never sent — which is the whole
    /// defect this level exists to remove.
    /// </summary>
    /// <remarks>
    /// Two rules, and the second is broader than the first on purpose. rippled's requirement flag
    /// describes the ledger object; the same models also carry <c>PreviousFields</c>,
    /// <c>FinalFields</c> and <c>NewFields</c>, which are partial projections — <c>PreviousFields</c>
    /// holds only the members a transaction changed, so even a Required field can be missing there.
    /// </remarks>
    [TestClass]
    public class TestUNullabilityConformance
    {
        private static Dictionary<string, Type> Models()
        {
            FieldInfo field = typeof(TestULedgerEntryFieldsConformance)
                .GetField("Models", BindingFlags.NonPublic | BindingFlags.Static);
            return (Dictionary<string, Type>)field.GetValue(null);
        }

        private static PropertyInfo FindProperty(Type model, string protocolField)
        {
            foreach (PropertyInfo property in model.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                JsonPropertyNameAttribute name = property.GetCustomAttribute<JsonPropertyNameAttribute>();
                string mapped = name?.Name ?? property.Name;
                if (string.Equals(mapped, protocolField, StringComparison.Ordinal))
                {
                    return property;
                }
            }

            return null;
        }

        private static bool CannotExpressAbsence(PropertyInfo property)
        {
            Type type = property.PropertyType;
            return type.IsValueType && Nullable.GetUnderlyingType(type) is null;
        }

        /// <summary>
        /// A field rippled declares Optional or Default must map to a property that can be absent.
        /// Authoritative: the requirement comes from the vendored ledger_entries.macro.
        /// </summary>
        [TestMethod]
        public void TestUOptionalProtocolFieldsMapToNullableProperties()
        {
            Dictionary<string, Dictionary<string, RippledLedgerEntryFormats.Requirement>> formats =
                RippledLedgerEntryFormats.Parse();
            Dictionary<string, Type> models = Models();
            List<string> offenders = new List<string>();

            foreach (KeyValuePair<string, Type> pair in models.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!formats.TryGetValue(pair.Key, out Dictionary<string, RippledLedgerEntryFormats.Requirement> fields))
                {
                    continue;
                }

                foreach (KeyValuePair<string, RippledLedgerEntryFormats.Requirement> field in fields)
                {
                    if (field.Value == RippledLedgerEntryFormats.Requirement.Required)
                    {
                        continue;
                    }

                    PropertyInfo property = FindProperty(pair.Value, field.Key);
                    if (property is not null && CannotExpressAbsence(property))
                    {
                        offenders.Add($"{pair.Key}.{field.Key} is {field.Value} but {property.PropertyType.Name} cannot be absent");
                    }
                }
            }

            Assert.AreEqual(
                0,
                offenders.Count,
                "a field the protocol allows to be absent must not re-serialize as a default:"
                    + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>
        /// Every value-typed property of a ledger-entry model must be nullable, whatever the
        /// protocol says about the object itself.
        /// </summary>
        /// <remarks>
        /// Broader than the rule above because these models double as the contents of
        /// PreviousFields/FinalFields/NewFields. PreviousFields carries only what a transaction
        /// changed, so any member can be missing there and a non-nullable property fabricates a
        /// value for it — that is where the 156 invented members on a ten-entry account_tx came from.
        /// </remarks>
        [TestMethod]
        public void TestULedgerEntryPropertiesCanAllExpressAbsence()
        {
            List<string> offenders = new List<string>();

            foreach (KeyValuePair<string, Type> pair in Models().OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                foreach (PropertyInfo property in pair.Value.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                    {
                        continue;
                    }

                    if (CannotExpressAbsence(property))
                    {
                        offenders.Add($"{pair.Key}.{property.Name} : {property.PropertyType.Name}");
                    }
                }
            }

            Assert.AreEqual(
                0,
                offenders.Count,
                "these appear in PreviousFields/FinalFields, where absence is normal:"
                    + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }
    }
}
```

- [ ] **Step 2: Прогнать, зафиксировать провал**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestUNullabilityConformance"
```
Expected: **оба теста красные** — первый перечисляет 9 нарушений, второй 80. Сохрани полный вывод второго: это рабочий список для Task 2.

- [ ] **Step 3: Коммит**

```bash
git add Tests/Xrpl.Tests/Models/TestUNullabilityConformance.cs
git commit -m "test(models): тест соответствия nullability — сейчас красный, фиксирует объём"
```

Красный тест в истории — намеренно: он документирует дефект до починки. Следующая задача делает его зелёным.

---

### Task 2: Ledger-модели — 80 свойств

**Files:**
- Modify: `Xrpl/Models/Ledger/*.cs`

- [ ] **Step 1: Править по списку из Task 1**

Каждое value-свойство ledger-модели: `uint` → `uint?`, `int` → `int?`, `bool` → `bool?`, `byte` → `byte?`, `DateTime` → `DateTime?`, enum → `Enum?`.

**Не трогай** свойства с `[JsonIgnore]` и вычисляемые (без сеттера) — тест их пропускает, и они не участвуют в сериализации.

Порядок — по файлам, начиная с крупнейших: `PayChannel` (6), `AccountRoot` (5), `FeeSettings` (5), `MPTokenIssuance` (5), `SignerList` (5), `LedgerHashes` (4), `Offer` (4).

- [ ] **Step 2: Прогнать тест соответствия**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestUNullabilityConformance"
```
Expected: оба зелёные.

- [ ] **Step 3: Починить сборку решения**

Смена типа сломает потребителей — `Sugar`, тесты, демо-проекты. Правило: где значение действительно нужно, `.Value` или `?? default` **с осознанным выбором дефолта**; где логика допускает отсутствие, — проверка на null.

**Не глуши `.Value` вслепую.** Если поле теперь может быть null, а код на это не рассчитан, — это находка, а не помеха: покажи её.

Run:
```bash
dotnet build XrplCSharp.sln -v q --nologo
```
Expected: 0 ошибок. Это гейт CI — проверяй именно решение, а не отдельные проекты.

- [ ] **Step 4: Прогон**

```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestU"
```
Expected: не менее 1020 (1018 + 2 новых), 0 падений.

- [ ] **Step 5: Коммит**

```bash
git add Xrpl/Models/Ledger Xrpl/Sugar Xrpl/Wallet Tests XrplCSharp.sln
git commit -m "fix(models)!: ledger-модели больше не приписывают нули отсутствующим полям"
```

---

### Task 3: Транзакции — 51 свойство

**Files:**
- Modify: `Xrpl/Models/Transactions/*.cs`

- [ ] **Step 1: Расширить тест на транзакции**

Добавь в `TestUNullabilityConformance` третий тест по образцу второго, но по моделям транзакций. Маппинг возьми из `TestUTxFormatConformance` тем же приёмом (рефлексия над приватным статическим полем), а требуемость — из `RippledTransactionFormats`.

Отдельно включи `NodeBase.LedgerEntryType`: спека фиксирует его как приписываемый в `PreviousFields`, а сам `NodeBase` не является ledger-моделью и во второй тест не попадает.

- [ ] **Step 2: Прогнать, зафиксировать провал, затем править**

Те же правила, что в Task 2.

- [ ] **Step 3: Сборка решения и прогон**

```bash
dotnet build XrplCSharp.sln -v q --nologo
```
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestU"
```

- [ ] **Step 4: Коммит**

```bash
git add Xrpl/Models/Transactions Tests
git commit -m "fix(models)!: модели транзакций и узлов метаданных не приписывают значений"
```

---

### Task 4: Остаток уровня 0 — `Id` и `Request`

**Files:**
- Modify: `Xrpl/Models/Subscriptions/BaseResponse.cs`, `Xrpl/Models/Subscriptions/ErrorResponse.cs`
- Modify: `Xrpl/Client/RequestManager.cs`

Замерено на уровне 0: конверт с `"id"` удерживает **3 672 B**, без него — **217 B**. Разница — `JsonElement`, который STJ строит для `object`-свойства, с невозвращаемой арендой из `ArrayPool`, на **каждом** ответе. `ErrorResponse.Request` — то же на ветке ошибок, ~360 B, а `Sugar/Submit.cs` ловит `txnNotFound` в цикле опроса.

- [ ] **Step 1: Тест бюджета**

Ужесточить `TestUEnvelopeRetainsNoMoreThanTheFrame`: порог 8192 стоял выше известного остатка. После этой задачи снять фактическое значение и вписать «измеренное + запас». Прогнать трижды.

- [ ] **Step 2: `Id`**

`RequestManager` уже приводит его к `Guid` через `Guid.TryParse($"{response.Id}")` — то есть форматирует `JsonElement` в строку на каждом ответе. Перевести на `JsonSlice` тем же `JsonSliceConverter` плюс `RawId`, либо на строгий тип. Выбор обосновать в коммите: `id` в протоколе может быть строкой или числом.

- [ ] **Step 3: `Request`**

`ErrorResponse.Request` — эхо запроса, содержимое произвольное. Перевести на `JsonSlice` + публичное `RawRequest` типа `RawJson`, ровно как сделано для `result`.

- [ ] **Step 4: Сборка, прогон, коммит**

---

### Task 5: `[JsonExtensionData]` — неизвестные поля перестают исчезать

**Files:**
- Modify: `Xrpl/Models/Ledger/BaseLedgerEntry.cs` и модели транзакций

- [ ] **Step 1: Проверить совместимость с конвертерами — сделать ДО правок**

`[JsonExtensionData]` не работает автоматически на типе с кастомным `JsonConverter`: конвертер сам управляет чтением. В репозитории такие есть — `LOConverter`, `ModifiedNodeConverter`, `CreatedNodeConverter`, `DeletedNodeConverter`, `TransactionResponseConverter`, `LONFTokenConverter`.

Прогнать разведку: добавить `[JsonExtensionData] public Dictionary<string, JsonElement> UnknownFields { get; set; }` в одну модель, скормить ответ с неизвестным полем и проверить, попало ли оно. Результат определяет объём Task 5: если конвертеры глушат extension data, их придётся учить ей, и это отдельная работа.

**Не переходить к Step 2, пока это не выяснено фактически.**

- [ ] **Step 2: По результату разведки — либо добавить, либо переоценить задачу**

Если extension data работает мимо конвертеров — добавить на `BaseLedgerEntry` и базовые модели транзакций, с тестом: ответ с полем, которого нет в модели, сохраняет его в `UnknownFields`.

Если не работает — остановиться, доложить, и решать отдельно: возможно, `Raw` уровня 1 закрывает потребность и extension data не нужна вовсе.

---

### Task 6: `CHANGES.md` и приёмка

- [ ] **Step 1: Раздел с кодом «было → стало»**

Обязательно показать самый частый случай: `uint x = entry.Sequence;` → `uint x = entry.Sequence ?? 0;` — и предупредить, что молчаливая подстановка нуля возвращает ровно тот дефект, ради которого всё делалось; там, где ноль не является осмысленным, нужна проверка.

Назвать числа: 9 прямых нарушений протокола, 80 + 51 свойство, 3 672 B на ответ от `Id`.

- [ ] **Step 2: Приёмка гейтом CI**

```bash
dotnet build XrplCSharp.sln -v q --nologo
```
Expected: 0 ошибок.

```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestU"
```

```bash
docker compose -f .ci-config/docker-compose.ci.yml up -d
```
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestI"
```
Expected: 265, 0 падений.
```bash
docker compose -f .ci-config/docker-compose.ci.yml down
```

---

## Что этот план сознательно не делает

- **Не разводит v1/v2** (`Amount`/`DeliverMax`, `tx`/`tx_json`, `meta`/`meta_blob`, `Tx()` с жёстким `api_version = 1`) — уровень 3.
- **Не добавляет CI-проверку fidelity** на корпусе живых ответов — уровень 4.
- **Не трогает стримы.** `subscribe` и догоны `path_find` идут через `EnqueueStreamMessage(Text())` и сырого текста не получили; отмечено финальным ревью уровня 1 как остаток.
- **Не выводит наружу `status`** конверта — уровень 1 вернул `ApiVersion`, `Warning`, `Warnings`, `Forwarded`, а `status` остался. Через `Raw` он недостижим (он вне `result`).
