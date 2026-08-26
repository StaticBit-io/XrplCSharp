# Raw Response, уровень 0: один парс и срез вместо JsonElement

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Убрать двойной разбор ответа и промежуточный `JsonElement`, сохранив вместо него границы поддерева `result` внутри уже существующего байтового кадра — чтобы уровень 1 мог отдать потребителю байт-точный исходный JSON без единой дополнительной аллокации.

**Architecture:** `BaseResponse.Result` (тип `object`, который System.Text.Json заполняет самодостаточным `JsonElement` с невозвращаемой арендой из `ArrayPool`) заменяется на `JsonSlice` — пару (offset, length) внутри кадра. Границы снимает конвертер через `Utf8JsonReader.TokenStartIndex` + `Skip()` + `BytesConsumed`, не материализуя поддерево. Кадр — это уже существующий точный `new byte[]` из `WebSocketClient.ReceiveLoop`; `RequestManager` связывает его со слайсом и десериализует целевой тип **напрямую из среза**, одним парсом.

**Tech Stack:** .NET 8/9/10, System.Text.Json, MSTest 4.0.2 (фильтр `TestU`), существующий стенд `Tests/Xrpl.Tests/Client/TestUResponseParsing.cs`.

**Две ловушки тестового проекта, проверенные на практике:**
- `Assert.ThrowsException<T>` в MSTest 4.0.2 **не существует** — в репозитории 26 использований `Assert.ThrowsExactly<T>` и 39 `Assert.ThrowsExactlyAsync<T>`. Использовать только их.
- `ImplicitUsings` в `Xrpl.Tests.csproj` выключен: `using System;` нужен явно, иначе методы-расширения вроде `ReadOnlySpan<byte>.SequenceEqual` не разрешаются.

**Замеры, ради которых всё делается** (реальный `account_tx`, 36 691 B на проводе):

| Представление | Байт на ответ |
|---|---|
| `JsonElement` — сейчас | 65 369 |
| кадр целиком (уже выделен сокетом) | 36 729 |

`JsonDocument.ParseValue` арендует 65 536 B и не возвращает в пул. После этой задачи промежуточного документа нет вовсе, а парс `result` выполняется один раз вместо двух.

---

## File Structure

**Создаются:**

- `Xrpl/Client/Json/JsonSlice.cs` — `readonly struct JsonSlice { int Offset; int Length; }`. Границы токена внутри буфера. Ничего не знает о буфере.
- `Xrpl/Client/Json/Converters/JsonSliceConverter.cs` — снимает границы через `Utf8JsonReader`, не материализуя поддерево. `Write` запрещён.
- `Xrpl/Client/Json/RawJson.cs` — `readonly struct RawJson` над `(byte[] frame, int offset, int length)`. Публичный тип, через который уровень 1 будет отдавать исходный JSON.
- `Tests/Xrpl.Tests/Client/Json/Converters/JsonSliceConverterTests.cs` — тесты конвертера и `JsonSlice`, класс `TestUJsonSliceConverter`, namespace `XrplTests.Client.Json.Converters`. **Расположение обязательно**: в репозитории 23 файла тестов конвертеров лежат именно там и следуют этому шаблону имён.
- `Tests/Xrpl.Tests/Client/Json/RawJsonTests.cs` — тесты `RawJson`, класс `TestURawJson`, namespace `XrplTests.Client.Json`. Путь теста зеркалит путь исходника — так устроены 177 файлов из 255.
- Тесты `BaseResponse.RawResult`/`Frame` **отдельного файла не получают**: они идут в существующий `Tests/Xrpl.Tests/Client/TestUResponseParsing.cs`, где уже живут тесты разбора ответа и есть хелперы `Pending<T>` и `BuildLedgerDataMessage`.

**Изменяются:**

- `Xrpl/Models/Subscriptions/BaseResponse.cs:38` — `object Result` → `JsonSlice ResultSlice` + `internal byte[]? Frame` + вычисляемое `RawJson RawResult`.
- `Xrpl/Client/RequestManager.cs:55-141, 500-517` — `HandleResponse` принимает кадр; `DeserializeResult` работает от `RawJson`.
- `Xrpl/Client/connection.cs:3223-3226` — передаёт `byte[]`, а не span.
- `Xrpl/Utils/Index.cs:60-63` — `HasNextPage` перестаёт быть всегда-`false`.
- `Tests/Xrpl.Tests/Client/TestUResponseParsing.cs` — подгонка под новые сигнатуры.

**Breaking — удаляется поимённо, без переходных мостиков:**

| Член | Судьба |
|---|---|
| `BaseResponse.Result` (`object`) | **удалить.** Замена: `ResultSlice` (границы) и `RawResult` (исходные байты) |
| `RequestManager.HandleResponse(ReadOnlySpan<byte>)` | **удалить.** Замена: `HandleResponse(byte[])` — span нельзя сохранить в поле, а кадр нужен на весь срок жизни ответа |
| `RequestManager.ParseEmptyObject()` (private) | **удалить** вместе с полем `EmptyResult` типа `JsonElement` |

Ни один из них не помечается `[Obsolete]` и не дублируется перегрузкой «как было»: политика мажора — всё, что уходит, уходит сразу (см. спеку, раздел «Политика разрыва»). В `Xrpl/` сейчас нет ни одного `[Obsolete]`, и этот план его не заводит.

Использований `BaseResponse.Result` внутри библиотеки, кроме `Index.cs:62` (сломанного), нет — проверено `grep`. Конверт ответа нигде не сериализуется — проверено `grep`, поэтому `JsonSliceConverter.Write` может кидать.

---

### Task 1: JsonSlice и конвертер границ

**Files:**
- Create: `Xrpl/Client/Json/JsonSlice.cs`
- Create: `Xrpl/Client/Json/Converters/JsonSliceConverter.cs`
- Test: `Tests/Xrpl.Tests/Client/TestURawResponseSlice.cs`

- [ ] **Step 1: Написать падающий тест на точность границ**

Создать `Tests/Xrpl.Tests/Client/TestURawResponseSlice.cs`:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// Pins that the response envelope records where <c>result</c> sits in the frame instead of
    /// materializing it. The slice has to be byte-exact: everything downstream — the typed
    /// deserialization and the raw JSON handed to consumers — is cut from it.
    /// </summary>
    [TestClass]
    public class TestURawResponseSlice
    {
        private sealed class SliceProbe
        {
            [JsonPropertyName("result")]
            [JsonConverter(typeof(JsonSliceConverter))]
            public JsonSlice Result { get; set; }
        }

        [TestMethod]
        public void TestUSliceMatchesResultSubtreeExactly()
        {
            // Deliberately irregular whitespace: the slice must reproduce the bytes as sent,
            // not a normalized rendering of them.
            string message = "{\"id\":\"7\", \"status\":\"success\", \"result\": {\"a\" : 1,\"b\":[2, 3]} , \"warning\":\"load\"}";
            byte[] frame = Encoding.UTF8.GetBytes(message);

            SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, new JsonSerializerOptions());

            string expected = "{\"a\" : 1,\"b\":[2, 3]}";
            string actual = Encoding.UTF8.GetString(frame, probe.Result.Offset, probe.Result.Length);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void TestUSliceIsEmptyWhenResultAbsent()
        {
            byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\",\"status\":\"success\"}");

            SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, new JsonSerializerOptions());

            Assert.IsTrue(probe.Result.IsEmpty);
        }

        [TestMethod]
        public void TestUSliceCoversExplicitNull()
        {
            byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\",\"result\":null}");

            SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, new JsonSerializerOptions());

            Assert.AreEqual("null", Encoding.UTF8.GetString(frame, probe.Result.Offset, probe.Result.Length));
        }
    }
}
```

- [ ] **Step 2: Запустить тест, убедиться что он не компилируется**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestURawResponseSlice"
```
Expected: ошибка компиляции — `JsonSlice` и `JsonSliceConverter` не существуют.

- [ ] **Step 3: Создать JsonSlice**

`Xrpl/Client/Json/JsonSlice.cs`:

```csharp
namespace Xrpl.Client.Json
{
    /// <summary>
    /// Where a JSON token sits inside the buffer it was read from, as a byte offset and length.
    /// Carries no reference to the buffer: the envelope that owns the frame pairs the two.
    /// </summary>
    public readonly struct JsonSlice
    {
        /// <summary>Byte offset of the first character of the token within the buffer.</summary>
        public int Offset { get; }

        /// <summary>Length of the token in bytes.</summary>
        public int Length { get; }

        /// <summary>True when no token was recorded — the member was absent from the buffer.</summary>
        public bool IsEmpty => Length == 0;

        public JsonSlice(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }
    }
}
```

- [ ] **Step 4: Создать JsonSliceConverter**

`Xrpl/Client/Json/Converters/JsonSliceConverter.cs`:

```csharp
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Records where a member sits in the frame instead of materializing it.
    /// </summary>
    /// <remarks>
    /// Deserializing <c>result</c> into <see cref="object"/> made System.Text.Json build a
    /// self-contained <see cref="JsonElement"/> for it, and <c>JsonDocument.ParseValue</c> rents
    /// the backing array from <see cref="System.Buffers.ArrayPool{T}"/> without ever returning it —
    /// 65 536 bytes for a 36 691-byte response, held for a subtree that was then parsed a second
    /// time to reach the requested type. Skipping the subtree and remembering its bounds costs
    /// nothing and leaves the single parse to the caller, straight out of the frame.
    /// </remarks>
    public sealed class JsonSliceConverter : JsonConverter<JsonSlice>
    {
        /// <inheritdoc />
        public override JsonSlice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            long start = reader.TokenStartIndex;
            reader.Skip();
            long end = reader.BytesConsumed;
            return new JsonSlice(checked((int)start), checked((int)(end - start)));
        }

        /// <summary>
        /// Always throws. A response envelope describes what a node sent; re-emitting it from the
        /// parsed form would produce a plausible but different document, which is the failure mode
        /// this type exists to remove.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, JsonSlice value, JsonSerializerOptions options)
        {
            throw new NotSupportedException(
                "A response envelope is not serializable: write the original bytes through RawJson instead.");
        }
    }
}
```

- [ ] **Step 5: Запустить тесты, убедиться что проходят**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestURawResponseSlice"
```
Expected: PASS, 3 теста.

Если `TestUSliceMatchesResultSubtreeExactly` падает с лишним хвостовым пробелом — значит `BytesConsumed` захватил разделитель; в этом случае обрезать хвостовые пробельные байты в `Read` перед возвратом. Проверить фактический вывод прежде чем менять код.

- [ ] **Step 6: Коммит**

```bash
git add Xrpl/Client/Json/JsonSlice.cs Xrpl/Client/Json/Converters/JsonSliceConverter.cs Tests/Xrpl.Tests/Client/TestURawResponseSlice.cs
git commit -m "feat(client): записывать границы result в кадре вместо материализации JsonElement"
```

---

### Task 2: RawJson — публичный доступ к срезу

**Files:**
- Create: `Xrpl/Client/Json/RawJson.cs`
- Test: `Tests/Xrpl.Tests/Client/TestURawResponseSlice.cs` (дополняется)

- [ ] **Step 1: Написать падающий тест**

Добавить в `TestURawResponseSlice` (внутрь класса, после существующих методов):

```csharp
        [TestMethod]
        public void TestURawJsonRendersTheOriginalBytes()
        {
            // `{"result": {"a" : 1} }` — the inner object starts at byte 11 and is 9 bytes long.
            byte[] frame = Encoding.UTF8.GetBytes("{\"result\": {\"a\" : 1} }");
            RawJson raw = new RawJson(frame, 11, 9);

            Assert.AreEqual("{\"a\" : 1}", raw.ToString());
            Assert.AreEqual(9, raw.Length);
            Assert.IsFalse(raw.IsEmpty);
        }

        [TestMethod]
        public void TestURawJsonDefaultIsEmpty()
        {
            RawJson raw = default;

            Assert.IsTrue(raw.IsEmpty);
            Assert.AreEqual(string.Empty, raw.ToString());
            Assert.AreEqual(0, raw.Span.Length);
        }
```

Дописать в шапку файла `using Xrpl.Client.Json;` — он уже добавлен в Task 1, проверить что он на месте.

- [ ] **Step 2: Запустить, убедиться что не компилируется**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestURawJson"
```
Expected: ошибка компиляции — `RawJson` не существует.

- [ ] **Step 3: Создать RawJson**

`Xrpl/Client/Json/RawJson.cs`:

```csharp
using System;
using System.Text;
using System.Text.Json;

namespace Xrpl.Client.Json
{
    /// <summary>
    /// The bytes a node actually sent for one member of a response, as they arrived.
    /// </summary>
    /// <remarks>
    /// A window onto the frame rather than a copy of it: the frame is the exact-sized array the
    /// receive loop already allocated, so holding this costs nothing beyond keeping that array
    /// alive. UTF-16 is never stored — <see cref="ToString"/> builds it on demand, which for a
    /// large response is twice the byte length and worth paying only when something needs text.
    /// </remarks>
    public readonly struct RawJson
    {
        private readonly byte[]? _frame;
        private readonly int _offset;
        private readonly int _length;

        public RawJson(byte[] frame, int offset, int length)
        {
            _frame = frame;
            _offset = offset;
            _length = length;
        }

        /// <summary>True when nothing was captured.</summary>
        public bool IsEmpty => _frame is null || _length == 0;

        /// <summary>Length of the captured JSON in bytes.</summary>
        public int Length => _frame is null ? 0 : _length;

        /// <summary>The captured JSON, as UTF-8, without copying.</summary>
        public ReadOnlySpan<byte> Span => _frame is null ? default : _frame.AsSpan(_offset, _length);

        /// <summary>Copies the captured JSON into a new array.</summary>
        public byte[] ToArray() => _frame is null ? Array.Empty<byte>() : Span.ToArray();

        /// <summary>Writes the captured JSON into <paramref name="writer"/> verbatim.</summary>
        public void WriteTo(Utf8JsonWriter writer)
        {
            if (writer is null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (_frame is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteRawValue(Span, skipInputValidation: true);
        }

        /// <summary>Materializes the captured JSON as text. Allocates; call only when text is needed.</summary>
        public override string ToString()
        {
            return _frame is null ? string.Empty : Encoding.UTF8.GetString(_frame, _offset, _length);
        }
    }
}
```

- [ ] **Step 4: Запустить тесты**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestURawResponseSlice"
```
Expected: PASS, 5 тестов.

- [ ] **Step 5: Коммит**

```bash
git add Xrpl/Client/Json/RawJson.cs Tests/Xrpl.Tests/Client/TestURawResponseSlice.cs
git commit -m "feat(client): RawJson — окно на исходные байты ответа без копии"
```

---

> ## ⚠ Задачи 3-6 атомарны: один коммит на всю группу
>
> Удаление `BaseResponse.Result` (Task 3) немедленно ломает сборку `RequestManager.cs`
> и `Index.cs`, поэтому между Task 3 и концом Task 6 репозиторий **не собирается** и
> тесты **не запускаются**. Промежуточных коммитов в этой группе нет: каждый из них
> был бы заведомо красным.
>
> Порядок внутри группы: правки Task 3 → Task 4 → Task 5 → Task 6, затем один прогон
> и один коммит в Task 6 Step 5. Тесты, написанные в Step 1 каждой задачи группы,
> пишутся сразу, но запускаются все вместе в конце — «падение» на этом отрезке
> означает ошибку компиляции, а не красный тест, и это ожидаемо.
>
> Для исполнителя-подагента: задачи 3-6 выдаются **одним заданием**, а не четырьмя.

### Task 3: BaseResponse хранит срез, а не JsonElement

**Почему `Frame` обязан остаться `internal`.** Границы, снятые конвертером, верны только когда ридер покрывал весь документ одним непрерывным буфером. На пути `JsonSerializer.Deserialize(Stream)` System.Text.Json парсит порциями и отдаёт конвертеру ридер над своим внутренним буфером — числа выходят относительно него, без исключения (замерено: на payload ~40 КБ offset 40012 вместо 40019).

Этот путь обезврежен конструкцией, а не проверкой: `Frame` помечен `internal` и `[JsonIgnore]`, поэтому внешний вызов `Deserialize<BaseResponse>(stream)` оставит его `null`, и `RawResult` вернёт пустое значение вместо мусора. Заполняет `Frame` только `RequestManager`, ровно там же, где разбирает кадр. Отсюда правило: **`Frame` не делать публичным и не заполнять нигде, кроме `HandleResponse`** — иначе защита исчезает.

**Files:**
- Modify: `Xrpl/Models/Subscriptions/BaseResponse.cs:36-39`
- Test: `Tests/Xrpl.Tests/Client/TestURawResponseSlice.cs` (дополняется)

- [ ] **Step 1: Написать падающий тест**

Добавить в `TestURawResponseSlice`, и дописать `using Xrpl.Models.Subscriptions;` к списку using в начале файла:

```csharp
        [TestMethod]
        public void TestUEnvelopeExposesRawResultBoundToTheFrame()
        {
            string message = "{\"id\":\"7\",\"status\":\"success\",\"result\":{\"marker\":\"AABB\",\"n\":1}}";
            byte[] frame = Encoding.UTF8.GetBytes(message);

            ErrorResponse envelope = JsonSerializer.Deserialize<ErrorResponse>(frame, XrplJsonOptions.Default);
            envelope.Frame = frame;

            Assert.AreEqual("{\"marker\":\"AABB\",\"n\":1}", envelope.RawResult.ToString());
        }
```

Дополнительно добавь тест инварианта из преамбулы задачи:

```csharp
        /// <summary>
        /// Bounds are only meaningful for a reader that covered one contiguous buffer, which the
        /// Stream overloads do not. That path is disarmed by construction rather than by a check:
        /// Frame is internal, so it stays null there and the raw result comes back empty instead
        /// of pointing at bytes that were never checked.
        /// </summary>
        [TestMethod]
        public void TestUEnvelopeParsedFromAStreamExposesNoRawResult()
        {
            byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\",\"result\":{\"a\":1}}");
            using MemoryStream stream = new MemoryStream(frame);

            ErrorResponse envelope = JsonSerializer.Deserialize<ErrorResponse>(stream, XrplJsonOptions.Default);

            Assert.IsTrue(envelope.RawResult.IsEmpty);
        }
```

Понадобится `using System.IO;`.

`Frame` объявлен `internal`, и это работает из тестов: `Xrpl/Xrpl.csproj:25` уже содержит `<InternalsVisibleTo Include="Xrpl.Tests" />`. Дополнительной настройки не требуется.

Дописать в шапку файла `using Xrpl.Models.Subscriptions;`.

- [ ] **Step 2: Запустить, убедиться что не компилируется**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestUEnvelopeExposesRawResult"
```
Expected: ошибка компиляции — `Frame` и `RawResult` не существуют.

- [ ] **Step 3: Заменить Result на срез**

В `Xrpl/Models/Subscriptions/BaseResponse.cs` дописать в список using:

```csharp
using Xrpl.Client.Json;
```

и заменить блок

```csharp
        /// <summary>
        /// (WebSocket only) The value success indicates the request was successfully received and understood by the server.<br/>
        /// Some client libraries omit this field on success.
        /// </summary>
        [JsonPropertyName("result")]
        public object Result { get; set; }
```

на

```csharp
        /// <summary>
        /// Where the <c>result</c> member sits inside <see cref="Frame"/>.
        /// </summary>
        /// <remarks>
        /// Deliberately not the parsed result: binding it to <see cref="object"/> made
        /// System.Text.Json build a <see cref="System.Text.Json.JsonElement"/> whose pooled backing
        /// array is never returned, and the member was then parsed a second time to reach the
        /// requested type. Recording bounds costs nothing and leaves exactly one parse, cut
        /// straight from the frame.
        /// </remarks>
        [JsonPropertyName("result")]
        [JsonConverter(typeof(JsonSliceConverter))]
        public JsonSlice ResultSlice { get; set; }

        /// <summary>
        /// The frame this response was read from. Set by <c>RequestManager</c> right after parsing;
        /// null on an envelope built by hand.
        /// </summary>
        [JsonIgnore]
        internal byte[]? Frame { get; set; }

        /// <summary>
        /// The <c>result</c> member exactly as the node sent it.
        /// </summary>
        [JsonIgnore]
        public RawJson RawResult =>
            Frame is null || ResultSlice.IsEmpty
                ? default
                : new RawJson(Frame, ResultSlice.Offset, ResultSlice.Length);
```

Дописать в тот же using-блок `Xrpl.Client.Json.Converters` — атрибут ссылается на конвертер.

- [ ] **Step 4: Убедиться, что сборка падает ровно там, где ожидается**

Run:
```bash
dotnet build Xrpl/Xrpl.csproj -v q --nologo 2>&1 | grep -E " error " | sed -E 's/.*\([A-Za-z]+\.cs)\(([0-9]+).*/:/' | sort -u
```
Expected: только `RequestManager.cs` и `Index.cs`. Любой третий файл в списке — незамеченное использование `BaseResponse.Result`; разобраться с ним, прежде чем идти дальше.

**Не коммитить**: группа 3-6 атомарна, коммит один и делается в Task 6 Step 5.

---

### Task 4: RequestManager — один парс из среза

**Files:**
- Modify: `Xrpl/Client/RequestManager.cs:55-141`, `:500-517`
- Test: `Tests/Xrpl.Tests/Client/TestUResponseParsing.cs` (дополняется)

Тесты этой задачи пишутся в `TestUResponseParsing.cs`, а не в новом файле: там уже есть приватные хелперы `Pending<T>(manager)` (`:65`) и `BuildLedgerDataMessage(Guid, int)` (`:29`), и переиспользовать их надёжнее, чем повторять. `XrplGRequest` — **вложенный** тип, обращаться к нему как `RequestManager.XrplGRequest`. `CreateGRequest` читает у объекта запроса свойство `Id` через рефлексию, поэтому анонимный объект туда передавать нельзя — только реальный request-тип, как это делает `Pending<T>`.

- [ ] **Step 1: Написать падающий тест**

Добавить в класс `TestUResponseParsing`:

```csharp
        /// <summary>
        /// The result member is no longer parsed on the way in — the envelope only records where it
        /// sits — so the typed deserialization now has to cut it straight out of the frame.
        /// </summary>
        [TestMethod]
        public void TestUTypedResultDeserializesFromTheSlice()
        {
            RequestManager manager = new RequestManager();
            RequestManager.XrplGRequest pending = Pending<LOLedgerData>(manager);

            manager.HandleResponse(Encoding.UTF8.GetBytes(BuildLedgerDataMessage(pending.Id, 3)));

            LOLedgerData result = (LOLedgerData)pending.Promise.GetAwaiter().GetResult();
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Marker);
            Assert.AreEqual("AABBCCDD", result.Marker.ToString());
        }

        /// <summary>
        /// A response carrying the raw frame must expose the result member byte for byte.
        /// </summary>
        [TestMethod]
        public void TestURawResultReproducesWhatTheNodeSent()
        {
            RequestManager manager = new RequestManager();
            RequestManager.XrplGRequest pending = Pending<LOLedgerData>(manager);

            string message = BuildLedgerDataMessage(pending.Id, 2);
            (BaseResponse response, bool handled) = manager.HandleResponse(Encoding.UTF8.GetBytes(message));

            Assert.IsTrue(handled);
            int start = message.IndexOf("\"result\":", StringComparison.Ordinal) + "\"result\":".Length;
            string expected = message.Substring(start, message.Length - start - 1);
            Assert.AreEqual(expected, response.RawResult.ToString());
        }
```

Дописать в шапку файла `using Xrpl.Models.Subscriptions;`.

- [ ] **Step 2: Запустить, зафиксировать провал**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestUTypedResultDeserializesFromTheSlice"
```
Expected: ошибка компиляции `RequestManager.cs` — `response.Result` больше не существует.

- [ ] **Step 3: Переписать разбор результата**

В `Xrpl/Client/RequestManager.cs` заменить блок `EmptyResult`/`ParseEmptyObject` (`:58-70`) на

```csharp
        /// <summary>
        /// Stands in for a missing <c>result</c>, matching what deserializing the literal
        /// <c>"{}"</c> used to produce.
        /// </summary>
        private static readonly byte[] EmptyResult = Encoding.UTF8.GetBytes("{}");
```

и дописать `using System.Text;` в шапку файла. Удалить метод `ParseEmptyObject`.

Заменить тело `DeserializeResult` (`:117-141`) на

```csharp
        /// <summary>
        /// Converts the <c>result</c> member of a response into the type the request was created
        /// with, parsing it straight out of the frame.
        /// </summary>
        /// <remarks>
        /// The member is not parsed before this point: the envelope only recorded where it sits.
        /// That leaves exactly one parse of the response body, against the UTF-8 the node sent,
        /// with no intermediate document and no pooled array left unreturned.
        /// </remarks>
        private object DeserializeResult(RawJson raw, Type type)
        {
            ReadOnlySpan<byte> json = raw.IsEmpty ? EmptyResult : raw.Span;

            // An explicit `"result": null` used to arrive as a missing member and produce an empty
            // object rather than null; keep that.
            if (json.SequenceEqual("null"u8))
            {
                json = EmptyResult;
            }

            return JsonSerializer.Deserialize(json, type, serializerOptions);
        }
```

В `Resolve` (`:91`) заменить

```csharp
                object deserialized = DeserializeResult(response.Result, taskInfo.Type);
```

на

```csharp
                object deserialized = DeserializeResult(response.RawResult, taskInfo.Type);
```

Дописать `using Xrpl.Client.Json;` в шапку файла.

- [ ] **Step 4: Переписать точки входа HandleResponse**

Заменить блок `:500-517` на

```csharp
        public (BaseResponse Response, bool Handled) HandleResponse(string message)
        {
            return HandleResponse(Encoding.UTF8.GetBytes(message));
        }

        /// <summary>
        /// Handles a message still in its wire form. This is the socket path.
        /// </summary>
        /// <remarks>
        /// The frame is kept rather than sliced away: the envelope records where <c>result</c> sits
        /// inside it, and both the typed deserialization and <see cref="BaseResponse.RawResult"/>
        /// are cut from those bounds. The array is the exact-sized one the receive loop already
        /// allocated, so keeping it costs nothing over what was allocated anyway.
        /// </remarks>
        public (BaseResponse Response, bool Handled) HandleResponse(byte[] frame)
        {
            ErrorResponse response = JsonSerializer.Deserialize<ErrorResponse>(frame, serializerOptions);
            response.Frame = frame;
            return HandleResponse(response);
        }
```

- [ ] **Step 5: Проверить, что `RequestManager.cs` из списка ошибок ушёл**

Run:
```bash
dotnet build Xrpl/Xrpl.csproj -v q --nologo 2>&1 | grep -E " error " | sed -E 's/.*\([A-Za-z]+\.cs)\(([0-9]+).*/:/' | sort -u
```
Expected: остался только `Index.cs` — он чинится в Task 6.

**Не коммитить**: группа 3-6 атомарна.

---

### Task 5: connection.cs передаёт кадр

**Files:**
- Modify: `Xrpl/Client/connection.cs:3223-3226`

- [ ] **Step 1: Проверить, что правка нужна**

Run:
```bash
dotnet build Xrpl/Xrpl.csproj -v q --nologo 2>&1 | grep -E "error" | head
```
Expected: либо чисто (неявное преобразование `byte[]` уже подходит), либо ошибка на `:3225` о несоответствии перегрузки.

- [ ] **Step 2: Если сборка чиста — пропустить задачу**

Вызов на `:3225` передаёт `utf8Message`, объявленный как `byte[]`. Прежняя перегрузка принимала `ReadOnlySpan<byte>` через неявное преобразование; новая принимает `byte[]` напрямую, поэтому вызов компилируется без изменений. Отметить шаг выполненным и перейти к Task 6.

- [ ] **Step 3: Если сборка падает — снять неоднозначность**

Заменить на `:3223-3226`

```csharp
                (data, handled) = utf8Message is null
                    ? requestManager.HandleResponse(message)
                    : requestManager.HandleResponse(utf8Message);
```

на

```csharp
                (data, handled) = utf8Message is null
                    ? requestManager.HandleResponse(message)
                    : requestManager.HandleResponse(frame: utf8Message);
```

- [ ] **Step 4: Проверить сборку**

Run:
```bash
dotnet build Xrpl/Xrpl.csproj -v q --nologo 2>&1 | grep -cE " error "
```
Expected: `0`

**Не коммитить**: группа 3-6 атомарна. Если файл не менялся — это ожидаемо, `byte[]` подходит под новую перегрузку без правок.

---

### Task 6: HasNextPage перестаёт быть всегда-false

**Files:**
- Modify: `Xrpl/Utils/Index.cs:60-63`
- Modify: `Tests/Xrpl.Tests/Utils/HasNextPage.cs` — сейчас это пустая заглушка

`HasNextPage` сравнивает `Result` с `Dictionary<string, object>`, а там всегда лежал `JsonElement` — метод возвращает `false` на любом ответе, включая страничные. Это тот же корневой дефект, и здесь он чинится естественно.

Почему это не было замечено: `Tests/Xrpl.Tests/Utils/HasNextPage.cs` — **пустой класс без `[TestClass]` и без единого теста**, заготовка под порт `hasNextPage.ts` из xrpl.js, которую не наполнили. Тесты пишутся туда, а класс переименовывается в `TestUHasNextPage`: его текущее полное имя `XrplTests.Xrpl.Utils.HasNextPage` не содержит `TestU`, поэтому под фильтром CI он не запустился бы даже с тестами внутри.

- [ ] **Step 1: Написать падающий тест**

Заменить содержимое `Tests/Xrpl.Tests/Utils/HasNextPage.cs` целиком:

```csharp
// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/utils/hasNextPage.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text;
using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Subscriptions;
using Xrpl.Utils;

namespace XrplTests.Xrpl.Utils
{
    /// <summary>
    /// Port of xrpl.js `hasNextPage.ts`. The class name carries the TestU prefix because the CI
    /// filter matches on the fully qualified name — as `HasNextPage` it would never have run.
    /// </summary>
    [TestClass]
    public class TestUHasNextPage
    {
        private static BaseResponse Envelope(string result)
        {
            byte[] frame = Encoding.UTF8.GetBytes($"{{\"id\":\"7\",\"status\":\"success\",\"result\":{result}}}");
            ErrorResponse envelope = JsonSerializer.Deserialize<ErrorResponse>(frame, XrplJsonOptions.Default);
            envelope.Frame = frame;
            return envelope;
        }

        [TestMethod]
        public void TestUMarkerPresentMeansMorePages()
        {
            Assert.IsTrue(Envelope("{\"marker\":\"AABB\",\"state\":[]}").HasNextPage());
        }

        [TestMethod]
        public void TestUMarkerAbsentMeansLastPage()
        {
            Assert.IsFalse(Envelope("{\"state\":[]}").HasNextPage());
        }

        /// <summary>The marker need not be first, and skipping over earlier members must not eat it.</summary>
        [TestMethod]
        public void TestUMarkerFoundAfterNestedMembers()
        {
            Assert.IsTrue(Envelope(
                "{\"state\":[{\"a\":{\"b\":[1,2]}}],\"ledger_index\":9,\"marker\":{\"ledger\":9,\"seq\":1}}")
                .HasNextPage());
        }

        /// <summary>A `marker` nested inside another member is not the paging marker.</summary>
        [TestMethod]
        public void TestUNestedMarkerIsNotThePagingMarker()
        {
            Assert.IsFalse(Envelope("{\"state\":[{\"marker\":\"AABB\"}]}").HasNextPage());
        }

        [TestMethod]
        public void TestUEnvelopeWithoutResultHasNoNextPage()
        {
            byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\",\"status\":\"success\"}");
            ErrorResponse envelope = JsonSerializer.Deserialize<ErrorResponse>(frame, XrplJsonOptions.Default);
            envelope.Frame = frame;

            Assert.IsFalse(envelope.HasNextPage());
        }

        /// <summary>An envelope built by hand carries no frame, so there is nothing to read.</summary>
        [TestMethod]
        public void TestUEnvelopeWithoutFrameHasNoNextPage()
        {
            Assert.IsFalse(new ErrorResponse().HasNextPage());
        }
    }
}
```

Тест `TestUNestedMarkerIsNotThePagingMarker` — причина, по которой в Step 3 стоит `reader.Read(); reader.Skip();` после каждого несовпавшего имени: без пропуска значения сканер нашёл бы вложенный `marker` и соврал.

- [ ] **Step 2: Запустить, зафиксировать провал**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestUHasNextPage"
```
Expected: FAIL — все три `Assert.IsTrue` не выполняются (метод возвращает `false` на любом входе), либо ошибка компиляции на `Result`.

- [ ] **Step 3: Переписать HasNextPage**

В `Xrpl/Utils/Index.cs` заменить

```csharp
        public static bool HasNextPage(this BaseResponse response)
        {
            return response.Result is Dictionary<string, object> dict && dict.ContainsKey("marker");
        }
```

на

```csharp
        /// <summary>
        /// True when the node reported a <c>marker</c>, meaning more pages follow.
        /// </summary>
        /// <remarks>
        /// Read off the raw result rather than a parsed projection: the previous form compared
        /// against <c>Dictionary&lt;string, object&gt;</c>, which the member never was, so it
        /// answered false for every response including paged ones.
        /// </remarks>
        public static bool HasNextPage(this BaseResponse response)
        {
            if (response is null || response.RawResult.IsEmpty)
            {
                return false;
            }

            Utf8JsonReader reader = new Utf8JsonReader(response.RawResult.Span);

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

                if (reader.ValueTextEquals("marker"u8))
                {
                    return true;
                }

                reader.Read();
                reader.Skip();
            }

            return false;
        }
```

Дописать `using System.Text.Json;` в шапку `Index.cs`, если его там нет.

- [ ] **Step 4: Запустить тест**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestUHasNextPage"
```
Expected: PASS.

- [ ] **Step 5: Коммит**

Это единственный коммит группы 3-6 — сюда входят `BaseResponse`, `RequestManager`, `connection` (если менялся) и `Index`.

Сначала полный прогон, потому что до этого момента тесты не запускались ни разу:

```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestU"
```
Expected: 0 падений, не менее 970 пройденных (964 базовых + 6 из `TestUHasNextPage`).

```bash
git add Xrpl/Models/Subscriptions/BaseResponse.cs Xrpl/Client/RequestManager.cs Xrpl/Client/connection.cs Xrpl/Utils/Index.cs Tests/Xrpl.Tests/Client/TestURawResponseSlice.cs Tests/Xrpl.Tests/Client/TestUResponseParsing.cs Tests/Xrpl.Tests/Utils/HasNextPage.cs
git commit -m "perf(client)!: разбирать result один раз из кадра, конверт хранит границы вместо JsonElement"
```

---

### Task 7: Зафиксировать выигрыш и отсутствие регресса

**Files:**
- Modify: `Tests/Xrpl.Tests/Client/TestUResponseParsing.cs`
- Test: тот же файл

- [ ] **Step 1: Прогнать весь unit-набор**

Run:
```bash
dotnet test --verbosity normal --settings test.runsettings --filter "TestU"
```
Expected: PASS целиком. **Базовая линия до начала работ — 964 пройденных, 0 падений** (снято 2026-08-17 на `net10.0`). После задач 1-6 число может только вырасти: ни один тест не удаляется, а `HasNextPage` добавляет шесть новых. Любое падение — регресс этой задачи, чинить до продолжения; не помечать шаг выполненным по частично зелёному прогону.

- [ ] **Step 2: Добавить тест бюджета удержания**

Добавить в `TestUResponseParsing` (внутрь класса):

```csharp
        /// <summary>
        /// The envelope must not retain more than the frame it was read from. Before the result
        /// member became a slice, System.Text.Json built a JsonElement for it whose pooled backing
        /// array — 65 536 bytes for a 36 691-byte response — was never returned to the pool.
        /// </summary>
        [TestMethod]
        public void TestUEnvelopeRetainsNoMoreThanTheFrame()
        {
            byte[] frame = Encoding.UTF8.GetBytes(BuildLedgerDataMessage(Guid.NewGuid(), 200));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetTotalMemory(true);

            const int Count = 50;
            List<BaseResponse> retained = new List<BaseResponse>(Count);
            for (int i = 0; i < Count; i++)
            {
                ErrorResponse envelope = JsonSerializer.Deserialize<ErrorResponse>(frame, XrplJsonOptions.Default);
                retained.Add(envelope);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long after = GC.GetTotalMemory(true);

            long perResponse = (after - before) / Count;
            GC.KeepAlive(retained);

            // The frame is shared, so an envelope on its own is a handful of fields — nowhere near
            // the pooled document the old shape kept alive.
            Assert.IsTrue(
                perResponse < 1024,
                $"envelope retained {perResponse} B on its own; a pooled result document is back");
        }
```

Дописать недостающие using (`System.Collections.Generic`, `Xrpl.Models.Subscriptions`, `Xrpl.Client.Json`) в шапку файла.

- [ ] **Step 3: Запустить тест бюджета**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestUEnvelopeRetainsNoMoreThanTheFrame"
```
Expected: PASS. Если падает — значит `Frame` где-то копируется вместо разделения ссылки; найти копию, а не поднимать порог.

- [ ] **Step 4: Прогнать интеграционные тесты**

Поднять стенд и прогнать полностью — конвейер разбора трогает каждый запрос:

```bash
docker compose -f .ci-config/docker-compose.ci.yml up -d
```

```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --verbosity normal --settings test.runsettings --filter "TestI"
```
Expected: PASS. Затем:

```bash
docker compose -f .ci-config/docker-compose.ci.yml down
```

- [ ] **Step 5: Обновить CHANGES.md**

Добавить в начало `CHANGES.md` раздел с таблицей «было → стало» — она заменяет собой отсутствующий совместимостный слой и потому обязана быть полной:

```markdown
### Breaking

| Было | Стало |
|---|---|
| `BaseResponse.Result` (`object`, на деле `JsonElement`) | `BaseResponse.RawResult` (`RawJson` — байты как их прислал узел); `BaseResponse.ResultSlice` — границы внутри кадра |
| `RequestManager.HandleResponse(ReadOnlySpan<byte>)` | `RequestManager.HandleResponse(byte[])` |

Переходных перегрузок и `[Obsolete]`-обёрток нет: удалённые члены удалены.

### Fixed

- `HasNextPage()` возвращал `false` на любом ответе, включая страничные: он сравнивал
  `Result` с `Dictionary<string, object>`, чем тот никогда не был.

### Performance

- `result` разбирается один раз вместо двух, прямо из кадра. Промежуточный
  `JsonElement` больше не строится — `JsonDocument.ParseValue` арендовал под него
  65 536 B на ответ в 36 691 B и не возвращал аренду в пул.
```

- [ ] **Step 6: Коммит**

```bash
git add Tests/Xrpl.Tests/Client/TestUResponseParsing.cs CHANGES.md
git commit -m "test(client): зафиксировать бюджет удержания конверта ответа"
```

---

### Task 8: Довести покрытие новых членов и ужесточить порог бюджета

**Files:**
- Modify: `Tests/Xrpl.Tests/Client/TestURawResponseSlice.cs`
- Modify: `Tests/Xrpl.Tests/Client/TestUResponseParsing.cs:186-215`

Задачи 1–7 покрывают основные пути. Здесь закрываются остатки: члены `RawJson`, запрет записи конверта, срез над не-объектными значениями, инвариант независимости кадров — и приводится в соответствие порог существующего теста бюджета, который после Уровня 0 станет заведомо слабым.

- [ ] **Step 1: Дописать тесты на непокрытые члены**

Добавить в класс `TestURawResponseSlice`:

```csharp
        /// <summary>An envelope built by hand has no frame, so there is nothing to hand out.</summary>
        [TestMethod]
        public void TestUEnvelopeWithoutFrameHasEmptyRawResult()
        {
            Assert.IsTrue(new ErrorResponse().RawResult.IsEmpty);
        }

        /// <summary>rippled always sends an object, but the slice must not assume it.</summary>
        [TestMethod]
        public void TestUSliceCoversNonObjectResults()
        {
            byte[] array = Encoding.UTF8.GetBytes("{\"result\":[1, 2]}");
            byte[] text = Encoding.UTF8.GetBytes("{\"result\":\"done\"}");
            byte[] number = Encoding.UTF8.GetBytes("{\"result\":42}");

            Assert.AreEqual("[1, 2]", Slice(array));
            Assert.AreEqual("\"done\"", Slice(text));
            Assert.AreEqual("42", Slice(number));

            static string Slice(byte[] frame)
            {
                SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, new JsonSerializerOptions());
                return Encoding.UTF8.GetString(frame, probe.Result.Offset, probe.Result.Length);
            }
        }

        /// <summary>
        /// Each response must read from its own frame. The receive loop hands out a fresh
        /// exact-sized array per message, and nothing downstream may collapse two of them.
        /// </summary>
        [TestMethod]
        public void TestUEnvelopesDoNotShareAFrame()
        {
            byte[] first = Encoding.UTF8.GetBytes("{\"id\":\"1\",\"result\":{\"n\":1}}");
            byte[] second = Encoding.UTF8.GetBytes("{\"id\":\"2\",\"result\":{\"n\":2}}");

            RequestManager manager = new RequestManager();
            (BaseResponse a, _) = manager.HandleResponse(first);
            (BaseResponse b, _) = manager.HandleResponse(second);

            Assert.AreEqual("{\"n\":1}", a.RawResult.ToString());
            Assert.AreEqual("{\"n\":2}", b.RawResult.ToString());
        }
```

Дописать в шапку файла `using System;`, `using System.Buffers;`, `using Xrpl.Client;`, `using Xrpl.Models.Subscriptions;`.

- [ ] **Step 2: Запустить дописанные тесты**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestURawResponseSlice"
```
Expected: PASS. `TestUEnvelopesDoNotShareAFrame` проходит с `handled = false` (запросов с такими id нет) — проверяется только `RawResult`, поэтому второй элемент кортежа отбрасывается.

- [ ] **Step 3: Измерить новый бюджет аллокаций**

Существующий `TestResponseParsingStaysWithinItsAllocationBudget` держит порог `ratio < 4.0`, рассчитанный на прежний двойной разбор. После Уровня 0 он станет заведомо слабым и перестанет что-либо ловить. Снять фактическое значение:

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestResponseParsingStaysWithinItsAllocationBudget" --logger "console;verbosity=detailed"
```
Тест печатает строку вида `response 1,234,567 bytes, 1.23 MB allocated per response (1.70x)`. Записать фактический `ratio`.

- [ ] **Step 4: Ужесточить порог**

В `TestUResponseParsing.cs` заменить

```csharp
            Assert.IsTrue(
                ratio < 4.0,
```

на порог, равный **измеренному в Step 3 значению плюс 0.5** — запас на дрожание GC между прогонами, но недостаточный, чтобы пропустить возврат второго разбора. Например, при измеренных `1.70x` записать `2.2`:

```csharp
            // Threshold tracks the single-parse path measured after the result member became a
            // slice. The old bound of 4.0 was sized for the double round-trip and would no longer
            // catch its return.
            Assert.IsTrue(
                ratio < 2.2,
```

Подставить своё измеренное число, а не 2.2, если оно отличается. Обновить и текст `<summary>` над тестом, чтобы он описывал текущий путь, а не прежний.

- [ ] **Step 5: Перепроверить порог трижды подряд**

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestResponseParsingStaysWithinItsAllocationBudget"
```
Повторить три раза. Expected: PASS все три. Если хоть один прогон падает — порог занижен, поднять на 0.3 и перепроверить; не отключать тест.

- [ ] **Step 6: Финальный полный прогон**

Run:
```bash
dotnet test --verbosity normal --settings test.runsettings --filter "TestU"
```
Expected: PASS целиком, 0 падений, и **не менее 964 + 22 = 986 пройденных**: 964 базовых (ни один не удалён) плюс 16 новых из `TestURawResponseSlice`, 6 из `TestUHasNextPage`. Два теста добавляются в `TestUResponseParsing`, поэтому фактическое число будет ещё выше. Если итог меньше 986 — тест потерян или не попал под фильтр; найти какой, прежде чем закрывать задачу.

- [ ] **Step 7: Коммит**

```bash
git add Tests/Xrpl.Tests/Client/TestURawResponseSlice.cs Tests/Xrpl.Tests/Client/TestUResponseParsing.cs
git commit -m "test(client): покрыть RawJson, срез над не-объектами и ужесточить бюджет аллокаций"
```

---

## Матрица покрытия

Каждый новый и изменённый член — и тест, который его держит. Пустых клеток быть не должно; если при исполнении появится член, которого здесь нет, тест на него обязателен.

| Член | Тест |
|---|---|
| `JsonSlice.Offset` / `.Length` | `TestUSliceMatchesResultSubtreeExactly` |
| `JsonSlice.IsEmpty` | `TestUSliceIsEmptyWhenResultAbsent` |
| `JsonSliceConverter.Read` — объект | `TestUSliceMatchesResultSubtreeExactly` |
| `JsonSliceConverter.Read` — `null` | `TestUSliceCoversExplicitNull` |
| `JsonSliceConverter.Read` — массив / строка / число | `TestUSliceCoversNonObjectResults` |
| `JsonSliceConverter.Read` — скобки внутри строкового значения | `TestUSliceSkipsBracesInsideStrings` (Task 1) |
| `JsonSliceConverter.Read` — смещения байтовые, не символьные | `TestUSliceOffsetsAreByteBased` (Task 1) |
| `JsonSliceConverter.Read` — под `XrplJsonOptions.Default` | `TestUSliceIsTheSameUnderProductionOptions` (Task 1) |
| `JsonSliceConverter.Read` — крупный кадр (защита от chunked-регресса) | `TestUSliceStaysExactOnALargeFrame` (Task 1) |
| `JsonSliceConverter.Read` — член отсутствует | `TestUSliceIsEmptyWhenResultAbsent` |
| `JsonSliceConverter.Write` кидает | `TestUWritingASliceIsRejected` (Task 1) |
| `RawJson.ToString` / `.Length` / `.IsEmpty` | `TestURawJsonRendersTheOriginalBytes` (Task 2) |
| `RawJson` — `default` | `TestURawJsonDefaultIsEmpty` (Task 2) |
| `RawJson.Span` алиасит кадр, не копирует | `TestURawJsonSpanAliasesTheFrame` (Task 2) |
| `RawJson.ToArray` отвязывает от кадра | `TestURawJsonToArrayDetachesFromTheFrame` (Task 2) |
| `RawJson` — окно нулевой длины над живым кадром | `TestURawJsonZeroLengthWindowIsEmpty` (Task 2) |
| `RawJson.WriteTo` — со срезом | `TestURawJsonWriteToEmitsTheBytesVerbatim` (Task 2) |
| `RawJson.WriteTo` — пустое окно и `default` | `TestURawJsonWriteToEmitsNullForAnEmptyWindow` (Task 2) |
| `RawJson.WriteTo` — null-writer | `TestURawJsonWriteToRejectsANullWriter` (Task 2) |
| `RawJson` — окно вне кадра, отрицательные значения | `TestURawJsonRejectsAWindowOutsideTheFrame` (Task 2) |
| `RawJson.Length` в байтах, не символах | `TestURawJsonLengthIsInBytes` (Task 2) |
| `RawJson` — равенство по идентичности окна | покрыто конструкцией `IEquatable<RawJson>`; отдельный тест не требуется |
| `BaseResponse.ResultSlice` | `TestUEnvelopeExposesRawResultBoundToTheFrame` |
| `BaseResponse.Frame` | `TestUEnvelopesDoNotShareAFrame` |
| `BaseResponse.RawResult` | `TestURawResultReproducesWhatTheNodeSent` |
| `BaseResponse.RawResult` без кадра | `TestUEnvelopeWithoutFrameHasEmptyRawResult` |
| `BaseResponse.RawResult` при разборе из `Stream` | `TestUEnvelopeParsedFromAStreamExposesNoRawResult` |
| `HandleResponse(byte[])` | `TestUTypedResultDeserializesFromTheSlice` |
| `HandleResponse(string)` | `TestUtf8AndStringOverloadsProduceTheSameResult` (существующий) |
| `DeserializeResult` — типизированный | `TestTypedRequestDeserializesFromTheParsedResultNode` (существующий) |
| `DeserializeResult` — `JsonElement` / `object` | `TestUntypedRequestGetsTheParsedResultNode` (существующий) |
| `DeserializeResult` — `result: null` | `TestResponseWithoutResultStillCompletes` (существующий) |
| `DeserializeResult` — `result` отсутствует | `TestResponseWithoutResultStillCompletes` (существующий) |
| ветка `status: "error"` | `TestErrorStatusRejectsWithTheParsedErrorResponse` (существующий) |
| `HasNextPage` — marker есть / нет | `TestUMarkerPresentMeansMorePages`, `TestUMarkerAbsentMeansLastPage` |
| `HasNextPage` — marker после вложенных членов | `TestUMarkerFoundAfterNestedMembers` |
| `HasNextPage` — вложенный marker не считается | `TestUNestedMarkerIsNotThePagingMarker` |
| `HasNextPage` — нет `result` / нет кадра | `TestUEnvelopeWithoutResultHasNoNextPage`, `TestUEnvelopeWithoutFrameHasNoNextPage` |
| `HasNextPage` — `result` не объект | `TestUNonObjectResultHasNoNextPage` |
| `HasNextPage` — пустой объект | `TestUEmptyResultObjectHasNoNextPage` |
| `HasNextPage` — экранированный ключ `marker` | `TestUEscapedMarkerKeyIsRecognized` |
| `HasNextPage` — ключи-почтисовпадения | `TestUNearMissKeysAreNotTheMarker` |
| владение кадром: ответ алиасит переданный массив | `TestUResponseAliasesTheFrameItWasGiven` |
| бюджет удержания конверта | `TestUEnvelopeRetainsNoMoreThanTheFrame` |
| бюджет аллокаций разбора | `TestResponseParsingStaysWithinItsAllocationBudget` (существующий, порог ужесточается) |

**Существующие тесты, которые обязаны остаться зелёными без правки логики** — проверено прогоном на реальном поведении System.Text.Json:

| Тест | Почему переживёт замену |
|---|---|
| `TestUntypedRequestGetsTheParsedResultNode` | `Deserialize(span, typeof(JsonElement))` даёт самодостаточный `JsonElement`, переживающий gen2 GC |
| `TestResponseWithoutResultStillCompletes` | `"{}"` даёт `ValueKind.Object` без `state`; типизированный путь даёт объект с `State == null` |
| `TestUtf8AndStringOverloadsProduceTheSameResult` | строковая перегрузка кодирует в UTF-8 и идёт тем же путём |
| `TestErrorStatusRejectsWithTheParsedErrorResponse` | ветка ошибки читает поля конверта, а не `result` |
| `TestCancellationToken` (`:66`, `:87`) | вызывает `HandleResponse(string)`, сигнатура сохранена |

`typeof(object)` отдельно проверен: `DictionaryObjectConverter` объявлен как `JsonConverter<Dictionary<string, object>>` и на `object` не распространяется, поэтому результат остаётся `JsonElement`, как и раньше.

---

## Найдено по ходу: `object` остался ещё в двух полях конверта

Ревью группы 3-6 замерило то, что этот план не покрывает. `result` переведён на срез, но в том же классе `JsonElement` живёт дальше:

| Поле | Цена сверх парса | Когда |
|---|---|---|
| `BaseResponse.Id` (`object?`) | ~248 B | **каждый** ответ |
| `ErrorResponse.Request` (`object`) | ~360 B | каждый ответ-ошибка |

Это та же невозвращаемая аренда `ArrayPool` из `JsonDocument.ParseValue`, только мелкими порциями. `Id` вдобавок форматируется через `Guid.TryParse($"{response.Id}")` — ещё строка на каждый ответ.

Отдельно неприятен `Request`: `Sugar/Submit.cs:425` ловит `RippledException` с `txnNotFound` в **цикле опроса** `SubmitAndWait`, то есть ~360 байт невозвращаемой аренды на каждый опрос неподтверждённой транзакции.

Заявка на отдельную задачу: `Id` привести к строгому типу (это либо строка, либо число — `Guid` в нашем случае), `Request` перевести на `JsonSlice` тем же конвертером. До тех пор формулировка «конверт хранит границы вместо JsonElement» верна лишь наполовину.

## Перенесено в уровень 1 (из финального ревью)

Ничего из этого не блокирует уровень 0, но должно быть сделано до или вместе с `XrplResponse<T>`:

1. **`AttachFrame(byte[])` вместо сеттера `Frame`.** Сейчас парность кадра и границ держится дисциплиной («сеттер зовут сразу после `Deserialize`, тем же массивом»), а проверка границ прогоняется на каждом обращении к `RawResult`. Метод, сверяющий `ResultSlice` с `frame.Length` один раз, делает инвариант структурным и `RawResult` бесплатным.
2. **Скан верхнего уровня вынести из `Utils/Index.cs` на `RawJson`** (`HasTopLevelProperty` / `TryGetTopLevelProperty`). `HasNextPage` — первый потребитель, уровень 1 будет вторым, внешние потребители третьим. Иначе цикл `Utf8JsonReader` перепишут трижды.
3. **`RawJson.Deserialize<T>()` / `ToJsonElement()`** — иначе каждый потребитель напишет `JsonSerializer.Deserialize(raw.Span, ...)` со своими опциями, мимо `XrplJsonOptions.Default`.
4. **Закрепить тестом сокетное число.** Главное достижение уровня 0 — 92 736 → 2 432 B на сообщение — не защищено ничем: это разовый замер в тексте. Нужен бюджет как отношение к длине сообщения, по образцу `TestTypedResponseParsingStaysWithinItsAllocationBudget`. Инфраструктура есть — `PagedResponseServer` и существующий тест через реальный сокет.
5. **Решить политику удержания кадра для `XrplResponse<T>`.** Каждый удержанный ответ пиннит весь кадр. Для `account_tx` кадр и есть `result`, но для постраничного обхода политику надо назвать явно — пиннить или `ToArray()`.
6. **`RawJson.WriteTo` на пустом окне пишет `null`.** Для API «воспроизвести, что прислал узел» отсутствующий член и `null` — не одно и то же; уровень 1 не должен звать это вслепую.

## Что этот план сознательно не делает

- Не добавляет `XrplResponse<T>` и не меняет сигнатуры 40 методов клиента — это уровень 1, отдельный план. Здесь только создаётся `RawJson`, на котором тот уровень будет построен.
- Не трогает nullability моделей и `[JsonExtensionData]` — уровень 2.
- Не разводит v1/v2 (`Amount`/`DeliverMax`, `tx`/`tx_json`, `meta`/`meta_blob`) — уровень 3.
- Не добавляет CI-проверку fidelity — уровень 4.
- Оставляет `ErrorResponse.Request` типом `object`: он заполняется только на ветке ошибок и там же и остаётся, поэтому аренда на нём редка. Перевести на срез можно позже, если это всплывёт в замерах.
