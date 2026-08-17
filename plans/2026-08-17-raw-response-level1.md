# Raw Response, уровень 1: `XrplResponse<T>` — конверт ответа как тип

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Отдать потребителю байт-точный JSON узла рядом с типизированной моделью и вернуть потерянный конверт ответа (`warnings`, `api_version`, `forwarded`), заменив возвращаемый тип 43 методов клиента на `XrplResponse<T>`.

**Architecture:** Уровень 0 уже сохранил границы `result` внутри кадра и отдаёт их через `BaseResponse.RawResult`. Здесь эта величина доводится до вызывающего: `RequestManager.Resolve` кладёт в промис не голый типизированный объект, а пару «типизированное + конверт», а `XrplClient.GRequest<T, R>` собирает из неё `XrplResponse<T>`. Все 43 типизированных метода делегируют в эту единственную точку, поэтому правится одна реализация и 43 сигнатуры.

**Tech Stack:** .NET 8/9/10, System.Text.Json, MSTest 4.0.2 (`Assert.ThrowsExactly`, не `ThrowsException`; `ImplicitUsings` выключен).

**Базовая линия перед началом:** 1001 unit-тест и 265 интеграционных, 0 падений.

---

## Решения, принятые до плана

**Неявных конверсий нет.** `XrplResponse<T>` не получает `implicit operator T`. Замерено: результат принимают через `var` в 273 местах и с явным типом в 248 — конверсия спасла бы меньше половины, оставив «совместимость через раз», которую мигрировать труднее, чем честный слом. Явное `.Result` вдобавок показывает в коде, что рядом есть ещё что-то.

**Переходных мостиков нет** (политика мажора, см. спеку): старые сигнатуры не сохраняются ни `[Obsolete]`-обёртками, ни перегрузками.

**Объём слома (замерено после Task 2-4, а не оценено):** 214 ошибок компиляции в тестах плюс 86 в единственном моке `IXrplClient`. Разбивка: 166 CS0029 (`XrplResponse<T>` не приводится к `T`), 34 CS1061 (места с `var`), 8 CS0023 (`?.` на `readonly struct` — обёртка не может быть null), 6 приведений.

**Ловушка при подсчёте:** пока `FeeTestClient` в `Tests/Xrpl.Tests/Sugar/TestUAutofillFees.cs` не реализует интерфейс, его 86 CS0738 **маскируют все остальные ошибки** — сборка показывает 86 и молчит про 214. Чинить мок надо первым, иначе объём работы не виден. Числа выше сняты временным исключением этого файла из компиляции.

---

## File Structure

**Создаются:**

- `Xrpl/Client/XrplResponse.cs` — `readonly struct XrplResponse<T>`: `Result`, `Raw`, `ApiVersion`, `Warnings`, `Forwarded`.
- `Xrpl/Client/ResolvedResponse.cs` — `internal sealed class ResolvedResponse`: пара «типизированный результат + конверт», единственное, что кладётся в промис.
- `Tests/Xrpl.Tests/Client/XrplResponseTests.cs` — класс `TestUXrplResponse`, namespace `XrplTests.Client`.

**Изменяются:**

- `Xrpl/Client/Json/RawJson.cs` — добавляются `Deserialize<T>()`, `ToJsonElement()`, `HasTopLevelProperty()`.
- `Xrpl/Models/Subscriptions/BaseResponse.cs` — сеттер `Frame` заменяется на `AttachFrame(byte[])`.
- `Xrpl/Client/RequestManager.cs:85, 301, 398` — в промис кладётся `ResolvedResponse`.
- `Xrpl/Client/IXrplClient.cs` — 43 сигнатуры в интерфейсе и 43 в реализации, плюс `GRequest` и `Request`.
- `Xrpl/Utils/Index.cs` — `HasNextPage` переходит на `RawJson.HasTopLevelProperty`.
- `Xrpl/Sugar/*.cs`, `Xrpl/Wallet/*.cs` — 10 файлов, 18 вызовов.
- Тесты — около 520 мест.
- `CHANGES.md`.

**Breaking — удаляется поимённо, без мостиков:**

| Член | Судьба |
|---|---|
| `Task<T> IXrplClient.<43 метода>(...)` | **сигнатура меняется** на `Task<XrplResponse<T>>` |
| `Task<T> IXrplClient.GRequest<T, R>(...)` | **меняется** на `Task<XrplResponse<T>>` |
| `Task<Dictionary<string, object>> IXrplClient.Request(...)` | **меняется** на `Task<XrplResponse<Dictionary<string, object>>>` |
| `BaseResponse.Frame` (сеттер) | **удалить**, заменить методом `AttachFrame(byte[])` |

---

### Task 1: Инфраструктура на `RawJson` и `AttachFrame`

Переносы 1–3 из финального ревью уровня 0. Делается первым: на этом стоит всё остальное.

**Files:**
- Modify: `Xrpl/Client/Json/RawJson.cs`
- Modify: `Xrpl/Models/Subscriptions/BaseResponse.cs`
- Modify: `Xrpl/Client/RequestManager.cs` (вызов `AttachFrame`)
- Modify: `Xrpl/Utils/Index.cs` (`HasNextPage` на общий скан)
- Test: `Tests/Xrpl.Tests/Client/Json/RawJsonTests.cs`

- [ ] **Step 1: Написать падающие тесты**

Добавить в `TestURawJson` (`Tests/Xrpl.Tests/Client/Json/RawJsonTests.cs`):

```csharp
    [TestMethod]
    public void TestURawJsonDeserializesWithLibraryOptions()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"ledger_index\":9,\"marker\":\"AABB\"}}");
        RawJson raw = new RawJson(frame, 10, frame.Length - 11);

        LOLedgerData typed = raw.Deserialize<LOLedgerData>();

        Assert.IsNotNull(typed);
        Assert.AreEqual("AABB", typed.Marker.ToString());
    }

    [TestMethod]
    public void TestURawJsonDeserializeOnAnEmptyWindowReturnsDefault()
    {
        Assert.IsNull(default(RawJson).Deserialize<LOLedgerData>());
    }

    [TestMethod]
    public void TestURawJsonToJsonElementOwnsItsData()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"a\":1}}");
        JsonElement element = new RawJson(frame, 10, 7).ToJsonElement();

        frame[11] = (byte)'z';
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        // The element is self-contained: it copied out of the frame rather than aliasing it.
        Assert.AreEqual(1, element.GetProperty("a").GetInt32());
    }

    [TestMethod]
    public void TestURawJsonToJsonElementOnAnEmptyWindowIsUndefined()
    {
        Assert.AreEqual(JsonValueKind.Undefined, default(RawJson).ToJsonElement().ValueKind);
    }

    [TestMethod]
    public void TestURawJsonFindsTopLevelPropertiesOnly()
    {
        Assert.IsTrue(Window("{\"marker\":1,\"a\":2}").HasTopLevelProperty("marker"u8));
        Assert.IsTrue(Window("{\"a\":{\"b\":[1,2]},\"marker\":1}").HasTopLevelProperty("marker"u8));
        Assert.IsFalse(Window("{\"a\":[{\"marker\":1}]}").HasTopLevelProperty("marker"u8));
        Assert.IsFalse(Window("{}").HasTopLevelProperty("marker"u8));
        Assert.IsFalse(Window("[1,2]").HasTopLevelProperty("marker"u8));
        Assert.IsFalse(default(RawJson).HasTopLevelProperty("marker"u8));
        Assert.IsTrue(Window("{\"\\u006darker\":1}").HasTopLevelProperty("marker"u8));

        static RawJson Window(string json)
        {
            byte[] frame = Encoding.UTF8.GetBytes(json);
            return new RawJson(frame, 0, frame.Length);
        }
    }
```

Дописать в шапку файла `using Xrpl.Models.Ledger;`.

И в `Tests/Xrpl.Tests/Client/TestUResponseParsing.cs`:

```csharp
        /// <summary>
        /// Pairing is done in one call that checks the bounds against the frame, so a frame that
        /// does not match the recorded slice is rejected where the two meet rather than lazily,
        /// inside a consumer's read.
        /// </summary>
        [TestMethod]
        public void TestUAttachFrameRejectsAFrameThatDoesNotFitTheSlice()
        {
            byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\",\"result\":{\"a\":1}}");
            ErrorResponse envelope = JsonSerializer.Deserialize<ErrorResponse>(frame, XrplJsonOptions.Default);

            Assert.ThrowsExactly<ArgumentException>(() => envelope.AttachFrame(Encoding.UTF8.GetBytes("{}")));
        }
```

- [ ] **Step 2: Запустить, зафиксировать провал**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestURawJson|TestUAttachFrame"
```
Expected: ошибка компиляции — `Deserialize`, `ToJsonElement`, `HasTopLevelProperty`, `AttachFrame` не существуют.

- [ ] **Step 3: Добавить члены в `RawJson`**

В `Xrpl/Client/Json/RawJson.cs`, после `ToArray()`:

```csharp
        /// <summary>
        /// Deserializes the captured JSON into <typeparamref name="T"/> using the library's
        /// serializer options.
        /// </summary>
        /// <remarks>
        /// Here so that a consumer does not reach for <c>JsonSerializer.Deserialize</c> with
        /// options of their own: the XRPL models depend on the converters in
        /// <see cref="XrplJsonOptions.Default"/>, and bare options silently produce a different
        /// object. Returns <c>default</c> for an empty window rather than throwing — an absent
        /// member is not a malformed one.
        /// </remarks>
        public T Deserialize<T>()
        {
            return IsEmpty ? default : JsonSerializer.Deserialize<T>(Span, XrplJsonOptions.Default);
        }

        /// <summary>
        /// Parses the captured JSON into a self-contained <see cref="JsonElement"/>.
        /// </summary>
        /// <remarks>
        /// The element copies out of the frame, so it stays readable after the frame is gone —
        /// unlike <see cref="Span"/>, which aliases it. An empty window yields
        /// <see cref="JsonValueKind.Undefined"/>.
        /// </remarks>
        public JsonElement ToJsonElement()
        {
            if (IsEmpty)
            {
                return default;
            }

            using (JsonDocument document = JsonDocument.Parse(ToArray()))
            {
                return document.RootElement.Clone();
            }
        }

        /// <summary>
        /// True when the captured JSON is an object carrying <paramref name="name"/> at its top
        /// level.
        /// </summary>
        /// <remarks>
        /// Each non-matching member's value is skipped whole, so a nested occurrence of the name
        /// cannot be mistaken for a top-level one. Matching goes through
        /// <see cref="Utf8JsonReader.ValueTextEquals(ReadOnlySpan{byte})"/>, which unescapes — a
        /// raw byte comparison would miss <c>\u006darker</c>.
        /// </remarks>
        public bool HasTopLevelProperty(ReadOnlySpan<byte> name)
        {
            if (IsEmpty)
            {
                return false;
            }

            Utf8JsonReader reader = new Utf8JsonReader(Span);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return false;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                if (reader.ValueTextEquals(name))
                {
                    return true;
                }

                reader.Skip();
            }

            return false;
        }
```

Дописать `using Xrpl.Client.Json;` не требуется — файл уже в этом namespace. Проверить, что `XrplJsonOptions` виден (тот же namespace).

- [ ] **Step 4: Заменить сеттер `Frame` на `AttachFrame`**

В `Xrpl/Models/Subscriptions/BaseResponse.cs` заменить

```csharp
        [JsonIgnore]
        internal byte[]? Frame { get; set; }
```

на

```csharp
        [JsonIgnore]
        private byte[]? _frame;

        /// <summary>
        /// Pairs this envelope with the frame it was read from.
        /// </summary>
        /// <remarks>
        /// One call instead of a settable property, so the bounds are checked against the buffer
        /// once, where the two meet — a frame that does not match the recorded slice is rejected
        /// here rather than lazily, inside a consumer's read of <see cref="RawResult"/>. Internal
        /// on purpose: the bounds are only meaningful for a reader that covered one contiguous
        /// buffer, which the Stream overloads of System.Text.Json do not, and keeping this
        /// unreachable disarms that path by construction.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="frame"/> is too short for the recorded slice.
        /// </exception>
        internal void AttachFrame(byte[] frame)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (!ResultSlice.IsEmpty
                && (ResultSlice.Offset > frame.Length || ResultSlice.Length > frame.Length - ResultSlice.Offset))
            {
                throw new ArgumentException(
                    $"Frame of {frame.Length} bytes does not contain the recorded result at "
                    + $"[{ResultSlice.Offset}, {ResultSlice.Offset + (long)ResultSlice.Length}).",
                    nameof(frame));
            }

            _frame = frame;
        }
```

и заменить `RawResult` на:

```csharp
        [JsonIgnore]
        public RawJson RawResult =>
            _frame is null || ResultSlice.IsEmpty
                ? default
                : new RawJson(_frame, ResultSlice.Offset, ResultSlice.Length);
```

Дописать `using System;` в шапку, если его нет.

- [ ] **Step 5: Обновить вызов в `RequestManager`**

В `HandleResponse(byte[] frame)` заменить `response.Frame = frame;` на `response.AttachFrame(frame);`.

- [ ] **Step 6: Перевести `HasNextPage` на общий скан**

В `Xrpl/Utils/Index.cs` заменить тело метода (весь ручной цикл `Utf8JsonReader`) на:

```csharp
        public static bool HasNextPage(this BaseResponse response)
        {
            return response is not null && response.RawResult.HasTopLevelProperty("marker"u8);
        }
```

XML-doc над методом оставить. Убрать `using System.Text.Json;`, если после правки он больше не используется — **проверить**, `JsonNode Decode` в этом файле может требовать `System.Text.Json.Nodes`, но не `System.Text.Json`.

- [ ] **Step 7: Прогон**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestU"
```
Expected: **1007 пройдено** (1001 + 5 в `TestURawJson` + 1 в `TestUResponseParsing`), 0 падений. Все десять тестов `TestUHasNextPage` обязаны остаться зелёными без правок — они и проверяют, что перенос скана ничего не изменил.

- [ ] **Step 8: Коммит**

```bash
git add Xrpl/Client/Json/RawJson.cs Xrpl/Models/Subscriptions/BaseResponse.cs Xrpl/Client/RequestManager.cs Xrpl/Utils/Index.cs Tests/Xrpl.Tests/Client/Json/RawJsonTests.cs Tests/Xrpl.Tests/Client/TestUResponseParsing.cs
git commit -m "feat(client): RawJson умеет разбор и скан верхнего уровня; кадр привязывается одним AttachFrame"
```

---

### Task 2: `XrplResponse<T>` и доставка конверта до вызывающего

**Files:**
- Create: `Xrpl/Client/XrplResponse.cs`, `Xrpl/Client/ResolvedResponse.cs`
- Modify: `Xrpl/Client/RequestManager.cs:85, 301, 398`
- Modify: `Xrpl/Client/IXrplClient.cs` (только `GRequest` и `Request`)
- Test: `Tests/Xrpl.Tests/Client/XrplResponseTests.cs`

- [ ] **Step 1: Написать падающий тест**

Создать `Tests/Xrpl.Tests/Client/XrplResponseTests.cs`:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text;

using Xrpl.Client;
using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace XrplTests.Client;

/// <summary>
/// The envelope a caller gets back: the typed projection and, beside it, the bytes the node sent.
/// The point of the pair is that the projection cannot be mistaken for the source — re-serializing
/// it drops members the model lacks and invents defaults for non-nullable CLR properties.
/// </summary>
[TestClass]
public class TestUXrplResponse
{
    [TestMethod]
    public void TestUCarriesResultAndRawSideBySide()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"ledger_index\":9,\"marker\":\"AABB\"}}");
        RawJson raw = new RawJson(frame, 10, frame.Length - 11);
        LOLedgerData typed = raw.Deserialize<LOLedgerData>();

        XrplResponse<LOLedgerData> response = new XrplResponse<LOLedgerData>(typed, raw, 2, null, false);

        Assert.AreSame(typed, response.Result);
        Assert.AreEqual("{\"ledger_index\":9,\"marker\":\"AABB\"}", response.Raw.ToString());
        Assert.AreEqual(2u, response.ApiVersion);
    }

    [TestMethod]
    public void TestUWarningsAreNeverNull()
    {
        XrplResponse<LOLedgerData> response = new XrplResponse<LOLedgerData>(null, default, null, null, false);

        Assert.IsNotNull(response.Warnings);
        Assert.AreEqual(0, response.Warnings.Count);
    }
}
```

- [ ] **Step 2: Запустить, зафиксировать провал**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestUXrplResponse"
```
Expected: ошибка компиляции — `XrplResponse<T>` не существует.

- [ ] **Step 3: Создать `XrplResponse<T>`**

`Xrpl/Client/XrplResponse.cs`:

```csharp
using System;
using System.Collections.Generic;

using Xrpl.Client.Json;
using Xrpl.Models.Subscriptions;

namespace Xrpl.Client
{
    /// <summary>
    /// A response from a node: the typed projection of its <c>result</c>, and the bytes that
    /// projection was made from.
    /// </summary>
    /// <remarks>
    /// The pair exists because the projection is lossy in both directions and cannot be turned
    /// back into what arrived: members the model does not know are dropped, and non-nullable CLR
    /// properties re-serialize as zeros that the node never sent. Anything that has to show or
    /// verify what a node actually said — a wallet rendering a transaction for signing — reads
    /// <see cref="Raw"/>; everything else reads <see cref="Result"/>.
    /// <para>
    /// There is deliberately no implicit conversion to <typeparamref name="T"/>. It would carry
    /// less than half the call sites (those with an explicit type; the ones using <c>var</c> break
    /// regardless), leaving a partial compatibility that is harder to migrate than a clean break,
    /// and it would hide that <see cref="Raw"/> exists at all.
    /// </para>
    /// </remarks>
    public readonly struct XrplResponse<T>
    {
        private readonly IReadOnlyList<RippleResponseWarning> _warnings;

        public XrplResponse(
            T result,
            RawJson raw,
            uint? apiVersion,
            IReadOnlyList<RippleResponseWarning> warnings,
            bool forwarded)
        {
            Result = result;
            Raw = raw;
            ApiVersion = apiVersion;
            _warnings = warnings;
            Forwarded = forwarded;
        }

        /// <summary>The <c>result</c> member, projected onto the requested type.</summary>
        public T Result { get; }

        /// <summary>
        /// The <c>result</c> member exactly as the node sent it. Empty when the response carried
        /// none.
        /// </summary>
        public RawJson Raw { get; }

        /// <summary>The API version the node answered on, when it reported one.</summary>
        public uint? ApiVersion { get; }

        /// <summary>
        /// Warnings the node attached to this response. Never null.
        /// </summary>
        /// <remarks>
        /// rippled attaches these under load and on a reporting-mode server, and before this type
        /// existed they did not survive the trip to the caller at all.
        /// </remarks>
        public IReadOnlyList<RippleResponseWarning> Warnings => _warnings ?? Array.Empty<RippleResponseWarning>();

        /// <summary>
        /// True when a Reporting Mode server forwarded this request to a P2P server and back.
        /// </summary>
        public bool Forwarded { get; }
    }
}
```

- [ ] **Step 4: Создать `ResolvedResponse`**

`Xrpl/Client/ResolvedResponse.cs`:

```csharp
using Xrpl.Models.Subscriptions;

namespace Xrpl.Client
{
    /// <summary>
    /// What a resolved request puts into its promise: the typed result and the envelope it came
    /// from, together.
    /// </summary>
    /// <remarks>
    /// The promise is <c>Task&lt;object&gt;</c> and <see cref="RequestManager"/> only knows the
    /// target type as a <see cref="System.Type"/>, so it cannot build a
    /// <see cref="XrplResponse{T}"/> itself. It carries both halves this far and the generic
    /// client assembles them, which keeps the manager free of the generic parameter.
    /// </remarks>
    internal sealed class ResolvedResponse
    {
        public ResolvedResponse(object result, BaseResponse envelope)
        {
            Result = result;
            Envelope = envelope;
        }

        public object Result { get; }

        public BaseResponse Envelope { get; }
    }
}
```

- [ ] **Step 5: Класть `ResolvedResponse` в промис**

В `Xrpl/Client/RequestManager.cs`:

`:85` — заменить
```csharp
                CompleteWithResult(taskInfo, deserialized);
```
на
```csharp
                CompleteWithResult(taskInfo, new ResolvedResponse(deserialized, response));
```

`:398` (в `CreateRequest`, путь `Request(Dictionary)`) — заменить
```csharp
            taskInfo.SetResult = result => task.TrySetResult((Dictionary<string, object>)result);
```
на
```csharp
            taskInfo.SetResult = result => task.TrySetResult(result);
```
и сменить тип `TaskCompletionSource<Dictionary<string, object>>` на `TaskCompletionSource<object>` в этом методе, а `XrplRequest.Promise` — с `Task<Dictionary<string, object>>` на `Task<object>`. Проверить фактические объявления в файле перед правкой и поправить согласованно.

`:301` менять не нужно — там уже `task.TrySetResult(result)`.

- [ ] **Step 6: Собрать `XrplResponse<T>` в клиенте**

В `Xrpl/Client/IXrplClient.cs` заменить реализацию `GRequest`:

```csharp
        public async Task<XrplResponse<T>> GRequest<T, R>(R request, CancellationToken cancellationToken = default) where R : BaseRequest
        {
            request.ApiVersion ??= ApiVersion;
            object resolved = await this.connection.GRequest<T, R>(request, cancellationToken: cancellationToken);
            return Wrap<T>(resolved);
        }

        /// <summary>
        /// Turns what the request manager resolved into the response handed to the caller.
        /// </summary>
        private static XrplResponse<T> Wrap<T>(object resolved)
        {
            ResolvedResponse carried = (ResolvedResponse)resolved;
            BaseResponse envelope = carried.Envelope;

            return new XrplResponse<T>(
                (T)carried.Result,
                envelope?.RawResult ?? default,
                envelope?.ApiVersion,
                envelope?.Warnings,
                envelope?.Forwarded ?? false);
        }
```

и объявление в интерфейсе (`:419`):

```csharp
        Task<XrplResponse<T>> GRequest<T, R>(R request, CancellationToken cancellationToken = default) where R : BaseRequest;
```

Аналогично `Request(Dictionary)` — интерфейс (`:418`) и реализация (`:906`) переходят на `Task<XrplResponse<Dictionary<string, object>>>`, а в конце реализации результат оборачивается тем же `Wrap<Dictionary<string, object>>`. Точный вид тела посмотреть в файле: там есть работа с `api_version` до отправки, её не трогать.

- [ ] **Step 7: Прогон только этой задачи**

Сборка `Xrpl` на этом шаге ещё падает — 43 метода возвращают `Task<T>`, а `GRequest` теперь отдаёт обёртку. Это ожидаемо и чинится в Task 3.

Run:
```bash
dotnet build Xrpl/Xrpl.csproj -v q --nologo 2>&1 | grep -cE " error "
```
Expected: ошибки только в `IXrplClient.cs` (43 метода) и в `Sugar`/`Wallet`. Убедиться, что среди них нет `RequestManager.cs` и `connection.cs` — если есть, доделать Step 5.

**Не коммитить**: Task 2 и Task 3 атомарны, коммит один в конце Task 3.

---

### Task 3: 43 сигнатуры клиента

**Files:**
- Modify: `Xrpl/Client/IXrplClient.cs` — интерфейс и реализация

- [ ] **Step 1: Заменить сигнатуры в интерфейсе**

Каждое объявление вида
```csharp
        Task<AccountInfo> AccountInfo(AccountInfoRequest request, CancellationToken cancellationToken = default);
```
становится
```csharp
        Task<XrplResponse<AccountInfo>> AccountInfo(AccountInfoRequest request, CancellationToken cancellationToken = default);
```

Правило: `Task<X>` → `Task<XrplResponse<X>>` для **всех** методов, делегирующих в `GRequest`. Не трогать: `IsConnected()`, `Connect()`, `Disconnect()`, `EnsureClassicAddress(string)`, `Dispose()`, сахарные методы, возвращающие вычисленные значения (`GetFeeXrp`, `GetLedgerIndex`, `GetXrpBalance` и подобные — они не проходят через `GRequest` напрямую; проверить каждый по телу реализации).

Список кандидатов получить командой:
```bash
grep -nE "return this\.GRequest<" Xrpl/Client/IXrplClient.cs
```
Каждая такая строка соответствует методу, чью сигнатуру надо сменить — и в реализации, и в интерфейсе.

- [ ] **Step 2: Заменить сигнатуры в реализации**

Тела не меняются: `return this.GRequest<AccountInfo, AccountInfoRequest>(request, cancellationToken);` продолжает работать, потому что `GRequest` теперь сам возвращает обёртку. Меняется только тип возврата у метода.

- [ ] **Step 3: Собрать**

Run:
```bash
dotnet build Xrpl/Xrpl.csproj -v q --nologo 2>&1 | grep -E " error " | sed -E 's/.*\\([A-Za-z]+\.cs)\(([0-9]+).*/\1/' | sort | uniq -c
```
Expected: `IXrplClient.cs` исчез из списка; остались `Sugar/*` и `Wallet/*` — они чинятся в Task 4.

**Не коммитить**: см. Task 2.

---

### Task 4: Внутренние потребители — `Sugar` и `Wallet`

**Files:**
- Modify: 10 файлов в `Xrpl/Sugar/` и `Xrpl/Wallet/` (18 вызовов)

- [ ] **Step 1: Починить по списку компилятора**

Каждый вызов вида
```csharp
            AccountInfo data = await client.AccountInfo(request, cancellationToken);
```
становится
```csharp
            AccountInfo data = (await client.AccountInfo(request, cancellationToken)).Result;
```

Файлы (по `grep`): `Sugar/Autofill.cs`, `Sugar/Balances.cs`, `Sugar/ComposeSugar.cs`, `Sugar/DomainAccess.cs`, `Sugar/GetFeeXrp.cs`, `Sugar/GetLedgerIndex.cs`, `Sugar/Submit.cs`, `Wallet/FundWallet.cs`, `Wallet/LoanSigningHelper.cs`, `Wallet/SponsorSigningHelper.cs`.

**Не менять при этом семантику.** Если вызов был в составе выражения (`(await client.X(...)).Field`), скобки уже есть — добавляется только `.Result`.

- [ ] **Step 2: Собрать всю библиотеку**

Run:
```bash
dotnet build Xrpl/Xrpl.csproj -v q --nologo 2>&1 | grep -cE " error "
```
Expected: `0`

- [ ] **Step 3: Коммит группы 2-4**

Это единственный коммит атомарной группы Task 2 → Task 4.

```bash
git add Xrpl/Client/XrplResponse.cs Xrpl/Client/ResolvedResponse.cs Xrpl/Client/RequestManager.cs Xrpl/Client/IXrplClient.cs Xrpl/Sugar Xrpl/Wallet Tests/Xrpl.Tests/Client/XrplResponseTests.cs
git commit -m "feat(client)!: методы возвращают XrplResponse<T> с сырым JSON и конвертом ответа"
```

---

### Task 5: Тесты

**Files:**
- Modify: тесты в `Tests/Xrpl.Tests/` — около 520 мест

- [ ] **Step 1: Починить по списку компилятора**

Run:
```bash
dotnet build Tests/Xrpl.Tests/Xrpl.Tests.csproj -v q --nologo 2>&1 | grep -E " error " | head -40
```

Правило то же: `X v = await client.M(...)` → `X v = (await client.M(...)).Result`; `var v = await client.M(...)` → либо `var v = (await client.M(...)).Result`, либо оставить обёртку, если тест дальше читает только поля результата — тогда обращения к полям получают `.Result`.

**Не ослаблять ассерты ради компиляции.** Если тест перестал проверять то, что проверял, — это находка, остановиться и сообщить.

- [ ] **Step 2: Прогон unit**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestU"
```
Expected: **1009 пройдено** (1007 после Task 1 + 2 в `TestUXrplResponse`), 0 падений. Меньше — значит тест потерян.

- [ ] **Step 3: Прогон интеграционных**

```bash
docker compose -f .ci-config/docker-compose.ci.yml up -d
```

```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestI"
```
Expected: **265 пройдено**, 0 падений.

```bash
docker compose -f .ci-config/docker-compose.ci.yml down
```

- [ ] **Step 4: Коммит**

```bash
git add Tests/
git commit -m "test: перевести вызовы клиента на XrplResponse<T>"
```

---

### Task 6: Закрепить сокетный бюджет тестом

Перенос 4 из финального ревью уровня 0: главное достижение — 92 736 → 2 432 B на сообщение — сейчас держится только текстом в `CHANGES.md`.

**Files:**
- Modify: `Tests/Xrpl.Tests/Client/TestUResponseParsing.cs`

- [ ] **Step 1: Написать тест**

Инфраструктура уже есть: `PagedResponseServer` используется в этом файле на строках 341, 378 и 436, и один из тех тестов уже гоняет страницы через `Connection` над реальным сокетом, читая process-wide счётчик и потому помеченный `[DoNotParallelize]`. Взять его как образец.

Новый тест должен отличаться от существующего одним: он меряет **байтовый** путь как отношение аллокаций к длине сообщения и падает, если отношение вырастет. Существующий разделяет байтовый и строковый пути порогами 2.18x/4.84x, но не защищает достигнутое здесь — 2 432 B на сообщение против 92 736 до уровня 0.

Порядок: написать тест с заведомо слабым порогом (например 3.0), прогнать, записать фактическое значение, вписать «измеренное + 0.5». Не угадывать и не переносить сюда числа из `CHANGES.md` — они сняты на другой форме сообщения.

- [ ] **Step 2: Прогнать трижды**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "AllocationBudget"
```
Трижды подряд, все три зелёные. Если мигает — тест меряет process-wide память и требует `[DoNotParallelize]`, как `TestUEnvelopeRetainsNoMoreThanTheFrame`.

- [ ] **Step 3: Коммит**

```bash
git add Tests/Xrpl.Tests/Client/TestUResponseParsing.cs
git commit -m "test(client): закрепить бюджет сокетного пути"
```

---

### Task 7: `CHANGES.md` и приёмка

**Files:**
- Modify: `CHANGES.md`

- [ ] **Step 1: Дописать раздел**

В существующий `## Unreleased` добавить уровень 1: смена возвращаемого типа 43 метода, `Request(Dictionary)`, `GRequest`, замена сеттера `Frame` на `AttachFrame`, новые публичные типы. Обязательно — код «было → стало», как это сделано для `Result`:

```csharp
// было
AccountInfo info = await client.AccountInfo(request);

// стало
AccountInfo info = (await client.AccountInfo(request)).Result;

// а теперь доступно и то, ради чего всё делалось
XrplResponse<AccountInfo> response = await client.AccountInfo(request);
string asTheNodeSentIt = response.Raw.ToString();
```

Отдельно назвать: почему нет `implicit operator` (с цифрами 273/248), и что `Warnings` теперь доходят до вызывающего, а раньше терялись.

**Не менять кодировку файла** — в `dev` он без BOM, проверить `head -c 3 CHANGES.md | xxd` после правки.

- [ ] **Step 2: Финальная приёмка**

```bash
dotnet build Xrpl/Xrpl.csproj -v q --nologo
```
Expected: 0 ошибок, все три TFM.

```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestU"
```
Expected: не менее 1009, 0 падений.

- [ ] **Step 3: Коммит**

```bash
git add CHANGES.md
git commit -m "docs(changes): уровень 1 — XrplResponse<T> и миграция вызовов"
```

---

## Матрица покрытия

| Член | Тест |
|---|---|
| `RawJson.Deserialize<T>()` | `TestURawJsonDeserializesWithLibraryOptions` |
| `RawJson.Deserialize<T>()` на пустом окне | `TestURawJsonDeserializeOnAnEmptyWindowReturnsDefault` |
| `RawJson.ToJsonElement()` самодостаточен | `TestURawJsonToJsonElementOwnsItsData` |
| `RawJson.ToJsonElement()` на пустом окне | `TestURawJsonToJsonElementOnAnEmptyWindowIsUndefined` |
| `RawJson.HasTopLevelProperty` | `TestURawJsonFindsTopLevelPropertiesOnly` |
| `BaseResponse.AttachFrame` отвергает чужой кадр | `TestUAttachFrameRejectsAFrameThatDoesNotFitTheSlice` |
| `HasNextPage` после переноса на общий скан | десять существующих `TestUHasNextPage` — обязаны пройти без правок |
| `XrplResponse<T>` несёт `Result` и `Raw` | `TestUCarriesResultAndRawSideBySide` |
| `XrplResponse<T>.Warnings` не null | `TestUWarningsAreNeverNull` |
| сокетный бюджет | Task 6 |
| весь конвейер end-to-end | 265 интеграционных |

---

## Что этот план сознательно не делает

- **Не трогает `BaseResponse.Id` и `ErrorResponse.Request`** — они всё ещё `object` и держат по 3 672 B и ~360 B невозвращаемой аренды. Это уровень 2, вместе с nullability.
- **Не отдаёт сырой конверт целиком** (`RawEnvelope` поверх всего кадра). Вся проблема из спеки лежит внутри `result`; конверт понадобится, только если появится потребитель для сырых `warnings`.
- **Не решает политику удержания кадра при постраничном обходе.** Каждый удержанный `XrplResponse<T>` пиннит весь кадр. Для `account_tx` кадр и есть `result`, так что вопрос встанет на мелких ответах, которые копят — тогда и назвать политику, задокументировав `Raw.ToArray()` как выход.
- **Не делает поля моделей nullable и не добавляет `[JsonExtensionData]`** — уровень 2.
- **Не разводит v1/v2** (`Amount`/`DeliverMax`, `tx`/`tx_json`, `meta`/`meta_blob`) — уровень 3.
