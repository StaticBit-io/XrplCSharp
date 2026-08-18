# Changes

## Unreleased

Level 0 and level 1 of the raw-response work, landing together. The goal of the whole effort: a consumer cannot currently get the text a node actually sent — only the typed model — and re-serializing that model differs from the original in both directions, dropping fields the model lacks and inventing zeros for non-nullable CLR properties. Level 1, the first entry below, is the API the effort was for: methods return `XrplResponse<T>`, pairing the typed projection with `Raw` — the `result` member exactly as the node sent it — so a consumer no longer has to choose between the model and the truth. Level 0, the entry after it, is the foundation `Raw` is built on and pays for itself on its own: the envelope now records where `result` sits in the frame instead of materializing it twice.

* **Methods now return `XrplResponse<T>`: the typed result and, beside it, the bytes the node sent** (**breaking**) — the point of the whole effort. A consumer could not get what a node actually said, only the model, and re-serializing that model differs from the original in both directions. Measured on live mainnet responses at `api_version = 2`: `close_time_iso`, `ctid`, `tx_json.DeliverMax` and `meta_blob` are dropped; `PreviousFields.Flags = 0`, `LedgerEntryType` and — on a Payment — `TransactionType = "AccountSet"` are invented, 156 fabricated members on a ten-entry `account_tx` alone. For a wallet rendering a transaction so a person can check what they are signing, that is false precision.
  * `XrplResponse<T>` carries `Result` (the projection), `Raw` (the `result` member exactly as sent), and the envelope the client used to unwrap and discard: `ApiVersion`, `Warning`, `Warnings`, `Forwarded`. `Warnings` is never null
  * **no implicit conversion to `T`, deliberately.** Measured against this codebase it would carry fewer than half the call sites — 248 with an explicit type against 273 using `var`, which break either way — leaving a partial compatibility harder to migrate than a clean break, and hiding that `Raw` exists at all
  * `RawJson` gained `Deserialize<T>()`, `ToJsonElement()` and `HasTopLevelProperty()`, so a consumer does not reach for `JsonSerializer` with options of their own — the XRPL models depend on the converters in `XrplJsonOptions.Default`, and bare options silently produce a different object
  * `BaseResponse.Frame` (settable) became `AttachFrame(byte[])`: bounds are checked once where the frame and the recorded slice meet, instead of lazily inside every read of `RawResult`
  * `warning` — the literal `"load"`, rippled's rate-limit signal — reaches the caller for the first time. It is not reachable through `Raw` either: that is the `result` member, while `warning` lives in the envelope around it

  **Migrating.** There is no `[Obsolete]` shim, so here is the move:

  ```csharp
  // was
  AccountInfo info = await client.AccountInfo(request);

  // now
  AccountInfo info = (await client.AccountInfo(request)).Result;

  // and what the work was for
  XrplResponse<AccountInfo> response = await client.AccountInfo(request);
  string asTheNodeSentIt = response.Raw.ToString();
  ```

  * **`Tx(...)` pins `api_version = 1` regardless of `ClientOptions.ApiVersion`** (`IXrplClient.cs:889`). Its `Raw` is therefore the honest text of a *v1* response. A caller on API v2 — a wallet checking what it is about to sign — has to call `TxV2(...)`, which maps `tx_json` and `meta` as siblings the way v2 sends them. Reconciling the two is a later level; until then this is the one place where picking the wrong method silently changes what `Raw` contains
  * **breaking:** 43 members change return type — 41 typed methods, `Request(Dictionary<string, object>)`, and `GRequest<T, R>` itself. `Connection.Request` and `Connection.GRequest<T, R>` change with them, and `RequestManager`'s `XrplRequest.Promise` / `XrplGRequest.Promise` now resolve to `ResolvedResponse` rather than the value directly. `ResolvedResponse` and the `XrplResponse.From<T>` unpacker are public for exactly that reason: `RequestManager` is public, and a caller working at that level has to be able to name what it gets back. `From<T>` has a second overload that takes a `ResolvedResponse` directly, for a caller that already has one off an awaited `Promise` — the mismatch the `object` overload can only catch at run time, as an `XrplException`, becomes a compile error through this one
  * **not affected:** the sugar methods — `GetXrpBalance`, `GetLedgerIndex`, `SubmitAndWait`, `GetOrderBook`, `Submit(ITransactionRequest, …)` — keep their existing return types. They were already handing back the typed result before this change and still do; nothing about them moved
  * `Request(...)` and `GRequest<T, R>(...)` are no longer `async` methods — they delegate straight to `Connection`'s versions of themselves. An argument-validation exception they raise now leaves the call synchronously, before a `Task` is even returned, rather than surfacing when the returned task is awaited
  * `XrplResponse<T>` gained `Deconstruct(out T, out RawJson)`, so `var (result, raw) = await client.AccountInfo(request)` works — the one-line fix for the call sites that broke hardest on this change, the ones using `var` — and `HasNextPage`, reading the same `marker` signal the removed `BaseResponse.HasNextPage()` extension used to, now reachable from the type a caller of the client's own methods actually holds
  * `TestUXrplResponse` proves the feature end to end over a socket: a scripted response with irregular whitespace and a member no model knows comes back byte for byte in `Raw`, while the same member is provably absent from the re-serialized `Result`. The envelope is asserted on both the typed and the untyped path
  * costs nothing measurable: `XrplResponse<T>` is a 56-byte readonly struct for a reference-typed `T` (`Unsafe.SizeOf`; 48 bytes only for a value-typed `T`, and none of the 43 methods are parameterized with one, so 56 is the figure that applies in practice) that lands directly in the async method's result field, and all three allocation budgets are unchanged after the switch — 1.89x on the `JsonElement` path, 5.57x typed, 2.18x over the socket

* **The `result` member was parsed twice and its intermediate document was never given back** (**breaking**) — `BaseResponse.Result` was typed `object`, which System.Text.Json fills with a self-contained `JsonElement`. Building it costs a `JsonDocument.ParseValue`, which rents its backing array from `ArrayPool` and never returns it: **65 536 bytes rented for a 36 691-byte response**, held for a subtree that was then deserialized a second time to reach the requested type. The envelope now records *where* `result` sits instead of materializing it, and the requested type is cut straight from those bytes — one parse, no intermediate document, nothing left unreturned.
  * `JsonSlice` (byte offset + length) and `JsonSliceConverter`, which reaches the bounds through `Utf8JsonReader.TokenStartIndex` / `Skip()` / `BytesConsumed` without materializing the subtree. `Write` throws `NotSupportedException`: a response envelope describes what a node sent, and re-emitting it from the parsed form is exactly the plausible-but-different document this work exists to remove
  * `RawJson` — a window onto the frame rather than a copy of it. The frame is the exact-sized `new byte[]` the receive loop already allocates per message, so holding the window costs nothing beyond keeping that array alive. UTF-16 is never stored; `ToString()` builds it on demand. `ToArray()` is the documented way to outlive the response without pinning the whole frame
  * `RequestManager.HandleResponse` takes the frame and pairs it with the bounds. **The `string` overload now encodes to UTF-8 first, and that is required, not incidental**: on `Deserialize(string)` System.Text.Json transcodes into a buffer of its own, so the bounds would be relative to that buffer, unpairable, and the result would be lost on every response. Measured, the explicit array is still cheaper than what it replaced — 94 016 → 54 200 B per message — because the old path paid the same transcode plus the unreturned rental
  * **breaking:** `BaseResponse.Result` is gone, replaced by `ResultSlice` (bounds) and `RawResult` (the bytes as sent). `RequestManager.HandleResponse(ReadOnlySpan<byte>)` is gone, replaced by `HandleResponse(byte[])` — a span cannot be stored, and the frame must now outlive the call. As a consequence `HandleResponse(null)` no longer compiles: it is ambiguous between the `string` and `byte[]` overloads. No `[Obsolete]` grace period, consistent with `Path.TypeHex` in 10.11.0.0
  * **ownership moved with the signature.** The returned response keeps the array and cuts `RawResult` from it, so a caller must not reuse or mutate the buffer it passed — a pooled or ring buffer would silently rewrite a response already handed out. `TestUResponseAliasesTheFrameItWasGiven` pins this
  * `Frame` is deliberately `internal`. The bounds are only meaningful for a reader that covered one contiguous buffer, which the `Stream` overloads do not — there the offsets come out relative to the chunk, wrong and with no exception to say so (measured: offset 40 012 instead of 40 019 on a 40 KB payload). Keeping the setter internal disarms that path by construction: an envelope a consumer deserializes from a stream has no frame, so `RawResult` comes back empty rather than pointing at bytes nobody checked
  * **migrating off `Result`.** There is no `[Obsolete]` shim, so here is the whole move. Reading a member:

    ```csharp
    // was
    JsonElement result = (JsonElement)response.Result;
    string marker = result.GetProperty("marker").GetString();

    // now — parse the bytes the node sent
    using JsonDocument document = JsonDocument.Parse(response.RawResult.Span);
    string marker = document.RootElement.GetProperty("marker").GetString();
    ```

    Or, for the whole member as a value: `JsonSerializer.Deserialize<T>(response.RawResult.Span, XrplJsonOptions.Default)`. `response.RawResult.ToString()` gives the text verbatim, and allocates a UTF-16 copy each call — hold the result if you need it twice
  * **`RawResult` is empty on any envelope you deserialize yourself.** It is populated only by `RequestManager` on the request path. Stream messages, `LedgerStreamResponse` and friends, and anything you hand to `JsonSerializer.Deserialize<BaseResponse>` come back with no frame and therefore an empty `RawResult` — by design, see the `internal Frame` note above. None of those paths read `result` before, either
  * **behaviour shift on a hand-assembled response.** `RequestManager.Resolve` used to fall back to `JsonSerializer.Deserialize(result.ToString(), type)` for a `BaseResponse` that was built rather than parsed off the wire. Such a response now has no frame, so `DeserializeResult` substitutes `{}` and the promise **completes successfully with a defaulted object** instead of carrying the assembled values. Unreachable through the client — the only caller of `Resolve` always has a frame — but `Resolve` is public, and the old fallback is gone
  * **the string path trades transit for retention.** Its total allocation drops, but roughly one message-length of that is no longer a transient buffer: the frame is retained for as long as the response is. Consumers holding many responses — a paged crawl kept in memory — should keep `RawResult.ToArray()` and let the frame go
  * measured per message on the socket path — the one production uses, since `Connection` binds `OnBinaryMessage`: **92 736 → 2 432 B, a 38x reduction**. End to end on the typed path, where the removed document actually shows: **7.45x → 5.57x** of the response size, 6.32 → 4.72 MB per 889 KB response
* **`HasNextPage()` returned false for every response, including paged ones** — it compared `Result` against `Dictionary<string, object>`, which that member never was; it held a `JsonElement`. The method has no callers inside the SDK, which is how it survived. It now scans the raw result with `Utf8JsonReader` for a top-level `marker`, skipping over each non-matching member's value so a nested `marker` cannot be mistaken for the paging one
  * its test file existed as an empty stub — a class with no `[TestClass]` and no tests, a started-and-abandoned port of xrpl.js `hasNextPage.ts`. Worse, its fully qualified name carried no `TestU`, so it would not have run under the CI filter even with tests in it. It is now `TestUHasNextPage` with ten tests, including an escaped `marker` key, near-miss keys, non-object results, and a `marker` nested inside an array of objects
* two allocation budgets, both measured rather than guessed. The existing one is annotated with what it cannot see: it asks for `JsonElement`, so one is built either way and the figure is identical before and after (1.89x). `TestTypedResponseParsingStaysWithinItsAllocationBudget` is the one that sees the change. `TestUEnvelopeRetainsNoMoreThanTheFrame` guards retention
* **known remainder, not fixed here:** `BaseResponse.Id` is still typed `object` and so still builds a `JsonElement` with an unreturned rental — measured at **3 672 B retained per envelope with an `id` against 217 B without one**, on every response. `ErrorResponse.Request` is the same defect on the error path, and `Sugar.SubmitAndWait` catches `txnNotFound` in a polling loop. Both are the next task; the retention budget is set above this known remainder and below a returning `result` document
* `LONFTokenPage.PreviousTxnLgrSeq` changed from `long` to `uint?` (**breaking**) — the field is `UInt32` per `Base/Xrpl.BinaryCodec/Enums/definitions.json`, matching every other ledger entry's `PreviousTxnLgrSeq`; the prior `long` was both wider than the protocol and inconsistent with its siblings


## 10.12.0.0 08/16/2026

* **`Request(Dictionary<string, object>)` never delivered the API version, so one client spoke two protocol versions** (**breaking**) — it stamped the version under `nameof(ApiVersion)`, literally `"ApiVersion"`. A dictionary is serialized verbatim, and rippled knows only `api_version`: it ignores unknown fields and answers on its default, API v1. Measured on mainnet, the three spellings are not equivalent — `api_version: 2` returns the v2 shape, while `"ApiVersion": 2` and no version field at all both return v1. So `client.AccountInfo(…)` went out as v2 while `client.Request(new Dictionary { ["command"] = "account_info" })` on the *same client* went out as v1, and response shapes differed between the two with nothing to signal it. The typed path was never affected: `BaseRequest.ApiVersion` carries `[JsonPropertyName("api_version")]`.
  * the key is now the wire name, and a version the caller put in the dictionary themselves is still respected. The junk `"ApiVersion"` field no longer rides along on every request
  * **breaking:** callers of the untyped path move from API v1 to whatever `ApiVersion` says, which defaults to 2 — response shapes change under code that did not change. This is the fix, not a side effect: the previous behaviour ignored the setting entirely. Callers who want v1 can put `["api_version"] = 1` in the dictionary or set `ApiVersion` on the client
  * `TestURequestApiVersion` reads what the client actually puts on the wire through a request-capturing WebSocket server — a field the node ignores cannot be seen from the response, which is how this survived. It pins the wire name on the untyped path, that an explicit `api_version` is not overwritten, and that both request paths of one client carry the same version. `WebSocketTestServerBase` gained the client-frame reader that `PagedResponseServer` had kept private, rather than a third copy of it
* **`TransactionStream` re-parsed the transaction on every read of it, and lost the hash under API v1** (**breaking**) — the same defect `TransactionSummary` was fixed for in 10.9.1.0, left standing on the stream side. `Transaction` was an expression-bodied property over two `object` members holding `JsonElement`s: `JsonSerializer.Deserialize<TransactionResponse>((TransactionJson ?? Proposed).ToString(), …)`. Three things wrong with that one line, on the busiest path the client has — every transaction of a `transactions` subscription:
  * **the transaction was rendered back to a string and parsed a second time.** It was already parsed: `TransactionJson`/`Proposed` are `object`, which System.Text.Json fills with a self-contained `JsonElement`. Same round trip as the one removed from `RequestManager.Resolve` below
  * **nothing was cached**, so the expression ran again on every access. Measured over 300 real mainnet stream messages: one read cost 4.94 KB (API v1) / 3.96 KB (v2), three reads cost exactly three times that — 14.82 KB and 11.89 KB. A consumer reading `TransactionType` and then `Hash` paid twice, and nothing in the property's signature said so
  * **the hash was unreachable under API v1.** rippled reports it at the top level under v2 but only inside the envelope under v1, and `Hash` was mapped to the top-level field alone, so `tx.Hash` was always `null` on v1 — which is what left the `Blazor-WebAssembly` demo printing no hash, since it requests `"ApiVersion": 1`. Verified against mainnet in both directions
  * a message carrying neither envelope threw `NullReferenceException` straight out of the property

  `TransactionStream` now follows `TransactionSummary`: `Transaction` is typed `TransactionResponse` and mapped to `tx_json`, a private set-only `TransactionV1` alias catches the API v1 `transaction` envelope, and `Hash` falls back to the envelope. The transaction is deserialized **once, with the message that carries it** — there is no second parse left to cache, and reading the property back is a field read. `ledger_index` and `ledger_hash` needed no fallback: rippled reports both at the top level in either version, which the captures confirm.
  * measured over the same 300 messages per version, allocation per message for the whole consumer flow — deserialize the message, then read the transaction off it: **16.44 → 13.75 KB** at one read and **26.32 → 13.75 KB** at three (API v1); **15.24 → 13.07 KB** and **23.17 → 13.07 KB** (API v2). The figure no longer moves with the number of reads at all, which is the point. Timings did not separate reliably on the measuring machine and are not quoted
  * the trade-off, stated plainly: deserializing the message alone went **up**, 11.50 → 13.75 KB (v1), because the transaction is now materialized eagerly instead of being left as a lazy `JsonElement`. A consumer that never touches `Transaction` pays about 2.25 KB more per message; one that touches it once or more pays 2.7–12.6 KB less
  * **breaking:** the public `object` properties `TransactionJson` and `Proposed` are gone — they existed only as raw envelopes for the getter to re-parse, and there is nothing left to re-parse. `Transaction` keeps its name and type and gains a setter. Consistent with the removal of `Path.TypeHex` in 10.11.0.0, no `[Obsolete]` grace period
  * `TestUTransactionStreamEnvelope` pins both envelopes, the hash under both versions, the message carrying neither, and that repeated reads allocate nothing and hand back the same instance
* **Every response was parsed twice and copied to UTF-16 twice** — the cost of reading a response, measured rather than reasoned about. `RequestManager.Resolve` did `JsonSerializer.Deserialize(response.Result?.ToString() ?? "{}", taskInfo.Type, ...)`. `BaseResponse.Result` is typed `object`, which System.Text.Json fills with a `JsonElement` that already owns a private copy of the `result` bytes — so `.ToString()` rendered that element back into a UTF-16 string and the serializer parsed the string a second time. On a `ledger_data` page at `limit=2048` (~1 MB) the four stages measured, per response, at: 1.97 MB for the UTF-16 copy of the message, 1.68 MB for the document built over it, 1.97 MB for the UTF-16 copy of the `result`, 1.68 MB for the second document — **7.30 MB, 7.42x the response size**, all four allocations past the 85 KB large-object threshold. Both halves are now gone:
  * `DeserializeResult` works off the parsed node: `element.Deserialize(type, options)` for a typed model, and the element itself when the request asked for `JsonElement` or `object`, which is what a consumer that needs the raw ledger objects asks for (the typed `LOLedgerData.State` drops unknown fields). A `BaseResponse` assembled by hand rather than parsed off the wire keeps the old string path. Behaviour is otherwise unchanged, including a missing or JSON-`null` `result`, which still yields what deserializing `"{}"` yielded
  * the socket path carries the frame as it arrived. `Connection` binds `OnBinaryMessage` instead of `OnMessageReceived`, `IsLikelyResponse` and `RequestManager.HandleResponse` have `ReadOnlySpan<byte>` overloads, and the UTF-16 string is materialized — once, lazily — only for what genuinely needs text: stream messages and the `OnWarning`/`OnServerWarning`/`OnError` callbacks. The `string` overloads stay for `Connection.OnMessage(string)` and for external callers
  * the warning callbacks no longer pay for listeners that are not there. rippled attaches `warning`/`warnings` to responses under load and on a reporting-mode server, and the dispatch built the UTF-16 text for them before checking whether `OnWarning`/`OnServerWarning` were subscribed — on such a server that is the removed allocation, back on every page. Measured with warnings on all 20 pages and nothing subscribed: 4.28x → 2.08x
  * the failure report survives the failure. A response that will not parse is most often a heap that has just run out, and materializing the message for `OnError` is then the largest allocation left on the path — if it throws, the notification is lost inside the handler and the consumer sees silence. The text is now built only when a handler is attached, and an `OutOfMemoryException` while building it falls back to a literal placeholder so the classification still goes out. `Connection.OnMessage(null)` also keeps its old route through `OnError` instead of throwing `ArgumentNullException` out of the entry point
  * measured end to end against a local WebSocket server, 600 `ledger_data` pages of ~1 MB: **8.32 → 2.68 MB allocated per response** (8.46x → 2.72x the payload), 11.49 → 7.96 ms per response, 87 → 126 responses/s, peak managed heap 42.4 → 20.8 MB, peak LOH 39.2 → 17.3 MB, peak working set 203.4 → 71.8 MB. Under a lowered `DOTNET_GCHeapHardLimit` the pre-fix path reproduced the production failure exactly — `XrplException: Failed to deserialize response for request <id>: Exception of type 'System.OutOfMemoryException' was thrown`, with `JsonElement.ToString()` at the top of the inner stack — at a ceiling the fixed path completes 15/15 pages under
  * the win is not specific to `ledger_data` or to `JsonElement`: the second parse was on the path of every command. The repo's own `BenchmarkLedgerDataCrawl`, which goes through `Request` → `Dictionary<string, object>`, drops from 22.9 to 14.8 MiB allocated per 2 MiB page (11.4x → 7.4x) with LOH ending at 38.2 instead of 115.4 MiB — it stays above the `JsonElement` figure because building a `Dictionary<string, object>` boxes every value, which this change does not address
  * `TestUResponseParsing` pins the behaviour that had to survive — the untyped node handed through is self-contained and readable after a forced gen2 collection, a typed model deserializes to the same values, the `string` and UTF-8 overloads agree, a missing `result` still completes, an `error` status still rejects with the parsed `ErrorResponse` attached, a null message does not throw out of the entry point — and holds two allocation budgets at 4x the response size. The first measures `RequestManager` alone, per thread so the class-parallel run cannot perturb it (1.89x now). The second runs 20 pages through `Connection` over a real socket, because nothing else in the suite can see *which* overload the client picks: it reads the process-wide counter and is therefore kept out of the parallel pass, and it separates the two paths with room on both sides — 2.18x as bound, 4.84x with the string callback bound instead. `PagedResponseServer` reuses one response frame per connection and rewrites the id in place so the server contributes nothing to what the client is measured on
* **`error` responses were deserialized a third time** — the `status == "error"` branch of `HandleResponse` re-parsed the whole message into an `ErrorResponse` inside a `try`/`catch` that swallowed everything, to build the exception's `Response`. The message had already been deserialized into an `ErrorResponse` at the top of the same method; the second parse only produced an equal copy, and on a large error payload it was a second large-object allocation on a path that is already failing

## 10.11.1.0 08/13/2026

* **Fix infinite recursion in `LONFTokenConverter.Write` — the metadata of an NFT transaction could not be serialized at all** — regression introduced in 10.3.0.0 with the `Newtonsoft.Json` → `System.Text.Json` migration; affects every release from 10.3.0.0 on. `JsonSerializer.Serialize(tx.Meta)` threw `JsonException: A possible object cycle was detected` for any transaction whose `AffectedNodes` contain an `NFTokenPage`, which is every `NFTokenMint`, `NFTokenBurn`, `NFTokenAcceptOffer` and `NFTokenModify` that touched a page. Verified against mainnet on all six NFT transaction types — the four above failed, `NFTokenCreateOffer` and `NFTokenCancelOffer` (no page in their metadata) went through:
  * The converter broke its own recursion the way the other polymorphic converters do — strip itself from `options.Converters` via `JsonSerializerOptionsCache.WithoutConverter<T>` and re-enter the serializer. That works only for a converter that is *registered in the list*. `LONFTokenConverter` is declared as a `[JsonConverter]` **attribute on the `NFToken` type itself** (`LONFTokenPage.cs`), and a converter attached to a type outranks the options list, so System.Text.Json handed the value straight back to `Write` no matter what the list looked like. The frame repeated until the writer hit `MaxDepth`. Raising `MaxDepth` is not a workaround: at 64 and 128 it is a catchable `JsonException`, at 256 the stack overflows and the process dies
  * `NFToken` has two fields, so `Write` now emits them directly instead of delegating. The wire shape is unchanged — `{"NFToken":{"NFTokenID":"…","URI":"…"}}`, the envelope `Read` already looks for — and the documented null behaviour is preserved by honouring `options.DefaultIgnoreCondition` rather than hard-coding one: `XrplJsonOptions.Default` (`WhenWritingNull`) omits a null `URI`, plain options keep it as `null`
  * The six other converter types that call `WithoutConverter` — `LOConverter`, `GenericStringConverter<T>`, `MetaBinaryConverter`, `LedgerBinaryConverter`, `TransactionRequestConverter` and `TransactionResponseConverter` — were audited against the same two conditions — declared as a type-level attribute **and** re-serializing that same declared type. None hit both. `LOConverter` is registered in the options list (its one attribute use is property-level) and writes the concrete runtime type; `GenericStringConverter<T>`, `MetaBinaryConverter`, `LedgerBinaryConverter` and `TransactionRequestConverter` are only ever attached to properties. The three node converters (`CreatedNodeConverter`, `ModifiedNodeConverter`, `DeletedNodeConverter`) do not call `WithoutConverter` at all and so are not among those six, but they are type-level and were checked for the same trap anyway: they serialize a *different* class (`value.NewFields.GetType()`). `TransactionResponseConverter` is the one other type-level case, and the same trap was already defused there by the `TransactionResponseUnknown` sentinel, so that no value ever carries the annotated type at runtime. Nothing else was changed
  * `TestULONFTokenConverter` had `Read` coverage only, which is how the bug survived. It now pins the written shape, both round trips (`URI` set and null), null handling under `XrplJsonOptions.Default` and under plain options, a multi-token `NFTokenPage`, and — the regression test proper — serializing a `Meta` carrying an `NFTokenPage` in `CreatedNode.NewFields`, `ModifiedNode.FinalFields`, `ModifiedNode.PreviousFields` and `DeletedNode.FinalFields`, since the page can arrive in any of them. All offline, on prepared JSON
* **WebSocket message assembly was quadratic in the number of receive chunks** — `ReceiveLoopAsync` grew a multi-chunk message with `byteResult = byteResult.Concat(buffer.Take(result.Count)).ToArray()`. Every chunk allocated a fresh array the size of everything received so far and refilled it one byte at a time through a LINQ enumerator, so a message split into *k* chunks copied roughly `k/2` times its own length; every intermediate array was well past the 85 KB threshold and therefore landed on the uncompacted large object heap. `ledger_data` at `limit=2048` is a few megabytes and arrives in dozens of chunks over a real link, which is exactly where the cost concentrates. Chunks are now `Buffer.BlockCopy`-ed into a scratch buffer that grows to the largest message on the connection and is reused from then on; a message that arrives whole in one chunk skips the scratch entirely, and both the receive buffer and that scratch buffer are rented from `ArrayPool` rather than allocated per connection (measured on .NET 10: the shared pool does hand back the same multi-megabyte array after a return, so this is a real saving and not just indirection). Measured on a local fragmenting WebSocket server, 300 messages of 2 MiB, allocation per message: 3.50x payload at one chunk, 6.50x at eight, 18.52x at thirty-two — now a flat 3.01x, which is the floor (the exact-sized `byte[]` plus the UTF-16 string handed to the callback). Through the full client stack, a 3000-page `ledger_data` crawl with each page arriving in 32 chunks and a consumer retaining every object: 100.7 s → 55.8 s, 158.4 GiB → 67.3 GiB allocated, 891 → 398 gen2 collections, and the last-decile-to-first-decile page time drops from 1.39x to 1.13x. `ReceiveChunkSize` was measured at 1 MiB and 64 KiB and left at 1 MiB — now that the buffer is pooled, shrinking it changed nothing outside run-to-run noise. `TestUWebSocketMessageAssembly` pins the byte-exactness of a 96-chunk message, that a short message after a long one picks up no stale bytes from the reused buffer, and that per-message allocation stays under 12x payload at 64 chunks (34.5x before the fix). A dead `timedOut` local, declared and tested but never assigned since it appeared, is gone
* **Request timeout timers outlived their requests** — `RequestManager.Resolve`/`Reject` called `timer.Stop()`. `System.Timers.Timer` derives from `Component` and carries a finalizer, so every completed request left a finalizable object behind, each of them holding its request's serialized text alive through the `Elapsed` closure; over a long paged crawl that is thousands of them. `Dispose()` stops the timer and takes it off the finalization queue. A second, worse case sat next to it: a token that is **already cancelled** runs its `Register` callback inline, so `Reject` completed the request in the middle of the factory method — before the timeout timer existed and therefore with nothing to remove. The factory then registered the timer for a promise that was already gone, and when it fired, `Reject` took its missing-promise early return without removing it, so the entry stayed in `timeoutsAwaitingResponse` for the life of the process. The `CancellationTokenRegistration` leaked on the same path, its assignment to `TaskInfo` happening after `DeletePromise` had already run. Both factories now check whether the promise survived and clean up after themselves; timer removal moved into `DisposeTimeout`, which is also called on the early returns of `Resolve` and `Reject` and so closes the narrow race with a concurrent cancellation as well. `TestURequestManagerCancellation` pins that an already-cancelled token leaves neither timer nor promise behind in either factory, and that a live request still arms its timeout and releases it on completion
* **Reflection on the per-response path is gone** — `Resolve`, `Reject` and `ObserveTaskException` reached for `TrySetResult`, `TrySetException` and `Task` through `GetType().GetMethod(...)` + `Invoke` on every single response. `TaskInfo` now carries typed `SetResult`/`SetException` delegates and the `CompletionTask` itself, wired when the request is created. The properties were added rather than substituted: `TaskInfo` is public, so instances built outside `RequestManager` keep the old reflective path
* **Dead `tasks` field removed from `XrplClient`** — `private readonly ConcurrentDictionary<int, TaskInfo> tasks` was never assigned and never read, so it was permanently null; a leftover from when the client tracked pending requests itself, which `RequestManager` has done for a long time

## 10.11.0.0 08/04/2026

* **MPT path steps (`0x40`)** — `PathSet` only knew the three classic hop-type bits (`0x01` account, `0x10` currency, `0x20` issuer). rippled added `STPathElement::TypeMpt = 0x40` in **3.2.0**, so a hop can now carry a 24-byte `MPTokenIssuanceID` instead of a currency. The gap was silent in both directions: `FromParser` matched none of its masks on a `0x40` byte, produced an empty hop and left the 24 MPTID bytes unread — every following byte was then parsed at the wrong offset — while `SynthesizeType` had no way to emit the bit at all. Now handled end to end:
  * `PathHop.MptIssuanceId` (`Hash192`) with a second constructor, `HasMpt()` and the `TypeMpt`/`TypeAll` byte constants; `currency` and `mpt_issuance_id` in one step throw `InvalidJsonException`, matching rippled, which throws `bad path element: MPT and Currency`
  * serialization order mirrors `STPathSet::add()` — type byte, then account(20), MPTID(24), currency(20), issuer(20)
  * `FromParser` now rejects what rippled rejects: a type byte carrying bits outside `TypeAll` (`0x71`), currency together with MPT, and an empty path — a leading or doubled `0xFF` separator, or a terminator that follows one. Previously any garbage byte was accepted and silently mis-parsed, and an empty path survived decoding but vanished on re-encoding, so the blob and the transaction hash no longer matched the bytes that were read
  * `ToBytes` throws on an empty `Path` instead of writing it away silently — the encoding side of the same asymmetry
  * a non-string `mpt_issuance_id` raises `InvalidJsonException` instead of a raw `InvalidOperationException` from the JSON node, matching how `Amount` and `Issue` report the same mistake
  * `Payment.IsPathStep` accepts `mpt_issuance_id` as a valid step asset and now follows rippled's `toStrand()` rules instead of the looser `xrpl.js` port it was: `account` combined with `currency`, `issuer` or `mpt_issuance_id`, and `currency` combined with `mpt_issuance_id`, are all `temBAD_PATH` upstream and are rejected before the transaction is sent. `xrpl.js` `isPathStep` still accepts `account` + asset — that is a gap on their side, not a compatibility requirement
  * `TestUPathSet` pins the layout of both the classic and the MPT hop against rippled's, plus the round trip and every rejection path; `TestUPathStep` pins the step-validation rules against `toStrand()`
  * Note this is ahead of the network: `MPTokensV2` is not enabled on mainnet (and not currently in `Majorities`), so MPT hops cannot yet appear in a validated ledger. `xrpl.js` and `xrpl-py` do not handle `0x40` either
* **`Path.MPTokenIssuanceID`** — the `mpt_issuance_id` key of a path step was missing from the model, so a step read from `ripple_path_find`/`path_find` could not be represented, let alone sent back
* **`Path.TypeHex` removed** (**breaking**, no `[Obsolete]` grace period, consistent with the 10.11.0.0 removal of ledger-object properties that are not protocol fields) — rippled removed `type_hex` from `STPath::getJson` in **1.7.0** (commit `f0724694`); only the unused `JSS(type_hex)` declaration survives in `jss.h`. No server has emitted the field for five years, so the property could never be anything but `null` — there is nothing to deprecate, only dead surface to delete. Verified against mainnet: 19 transactions carrying `Paths` across three consecutive ledgers, 21 path steps, `type` present in all 21 and `type_hex` in none, plus `ripple_path_find` on `s1`/`s2.ripple.com`. A response from a pre-1.7.0 server still deserializes — the unmapped key is ignored, which `TestUPathStepIgnoresLegacyTypeHex` pins
* **`Path.Type` is a `[Flags]` enum now** (**breaking**) — the hop type is a bitmask, but the model spelled it as a bare `int?`, so callers compared against magic `48`. It is now `PathStepType` (`Xrpl.Models.Enums`), matching how ledger objects already type their flags (`AccountRootFlags` and eight more) and how `TransactionType`/`LedgerEntryType` already exist model-side next to their codec counterparts. The enum is deliberately **not** shared with `Xrpl.BinaryCodec`: the codec stays byte-level — `PathHop.Type` is a `byte` synthesized from the `TypeAccount`/`TypeCurrency`/`TypeIssuer`/`TypeMpt` constants — so the model does not drag a codec namespace into its public surface. The wire format is unchanged: `XrplJsonOptions` deliberately registers no global `JsonStringEnumConverter` because XRPL protocol enums are numeric, and a value carrying a bit the enum does not declare survives deserialization untouched, which `TestUPathStep` pins along with the numeric wire form. The one behavioural loss: `"type":"48"` sent as a *string* no longer parses, since `NumberHandling.AllowReadingFromString` does not apply to enums; rippled always sends it as a number
* **`Path.Type` documented as read-only** — the XRPL docs mark it deprecated, but every rippled version still emits it on every step, so it stays. What the doc comment now states is that it is ignored on the way out: rippled's `STParsedJSON` reads only `account`/`currency`/`mpt_issuance_id`/`issuer` from a submitted step, and the binary codec derives the byte from the fields actually present. Pinned by `TestUPathSetHopTypeIsSynthesizedNotReadFromJson` — dropping `type`, or setting a deliberately wrong one, must not change the blob
* **rippled 3.3.0 — the CI stand and two protocol surfaces the SDK had modelled from a stale `develop` snapshot** (**breaking**). The stand moves from 3.2.1 to the 3.3.0 release image, which activates `BatchV1_1`, `Sponsor`, `PermissionDelegationV1_1`, `DynamicMPT`, `ConfidentialTransfer` and `fixCleanup3_3_0` at genesis, so 44 previously `AmendmentGuard`-skipped integration tests run for real on every CI run. 17 of them failed there: both features had been implemented against the nightly build the stand is pinned to (`3.3.0~b1+202607110018`, 11 Jul 2026) and upstream changed their shape before the release. Neither change is visible in `definitions.json` field codes alone, which is why nothing caught it earlier — see the Definitions Watch note below:
  * **DynamicMPT: `MutableFlags` is now `ImmutableFlags`, with the meaning inverted.** Same `UInt32` field, same nth 53, same bit values — but a set bit no longer means "this may be changed later", it means "this is frozen forever" (`rippled` `MPTokenIssuanceSet::preclaim`: `isImmutable(flag) => currentImmutableFlags & flag`). An issuance created without the field is therefore fully mutable, where before it was fully immutable — the exact opposite default. Because the field code did not change, the old models produced a blob the node accepted and then read backwards: mutations came back `tecNO_PERMISSION`, freezes silently succeeded, and `ledger_entry` returned an `ImmutableFlags` key the model did not bind. `MPTokenIssuanceCreateMutableFlags` and `MPTokenIssuanceSetMutableFlags` are replaced by a single `MPTokenIssuanceImmutableFlags` (`tif*`, aliasing the `lsif*` ledger constants) shared by both transactions and `LOMPTokenIssuance.ImmutableFlags`
  * **DynamicMPT: enabling a capability moved from a field to transaction flags.** The old `MPTokenIssuanceSet.MutableFlags = tmfMPTSet*` no longer exists; a capability is now enabled through `Flags = tfMPTSetCanLock | tfMPTSetRequireAuth | tfMPTSetCanEscrow | tfMPTSetCanTrade | tfMPTSetCanTransfer | tfMPTSetCanClawback | tfMPTSetCanHoldConfidentialBalance` (0x04–0x100), added to `MPTokenIssuanceSetFlags` next to the existing `tfMPTLock`/`tfMPTUnlock`. `ImmutableFlags` on the same transaction now does the opposite job — it freezes capabilities and fields, OR-ed into the ledger object, never cleared
  * **Sponsor: `SponsorshipSet` takes deltas, not absolute values.** `FeeAmount` and `RemainingOwnerCount` are fields of the `Sponsorship` **ledger object** only; the transaction carries `FeeAmountDelta` (`Amount`, nth 34) and `RemainingOwnerCountDelta` (**`Int32`**, nth 2) — signed changes applied to what the object already holds. Sending the old fields is not a semantic mismatch but a hard parse error: `STObject::applyTemplate` rejects any field outside the format with `invalidTransaction — Field 'FeeAmount' found in disallowed location`, which is what 13 of the 17 failures were. `SponsorshipSet.FeeAmount`/`RemainingOwnerCount` become `FeeAmountDelta` (`Currency`) / `RemainingOwnerCountDelta` (`int?`, signed — a negative delta reclaims budget); `LOSponsorship` is unchanged, it already matched the object. Client-side validation follows `SponsorshipSet::preflight`: a delta must be non-zero, `FeeAmountDelta` must be XRP, and `tfDeleteObject` may not carry any of the three modification fields
  * `definitions.json` + the three generated `Field.*` partials carry the renamed and the two new fields; `Common.TryGetInt32` was added for the signed delta, the codec already had `Int32Type`
* **The nightly pin now has a watcher** — `nightly-pin-watch.yml`, weekly. The pin is what `definitions-watch` sees as "develop", so leaving it in place quietly narrows that check to whatever rippled looked like when the pin was last touched; the two 3.3.0 renames above sat undetected behind a pin from 11 July. Dropping the pin is not an option — the nightly build timestamp shrank from 14 to 12 digits mid-2026, so Debian version ordering ranks old builds above new ones and an unpinned install gets a stale binary:
  * `.ci-config/bump-nightly-pin.sh` does the move: newest `xrpld` build from the nightly apt channel, `ARG XRPLD_VERSION` rewritten, `rippled.batchv11.cfg` regenerated from the develop commit **encoded in that version string** — config and binary cannot drift apart, which is the failure mode the old manual two-step invited. `--check` reports the pin, the newest build and the pin's age without touching anything. Both timestamp formats are compared by their common `YYYYMMDDHHMM` prefix
  * the workflow bumps only once the pin is older than `MAX_PIN_AGE_DAYS` (21) — nightly publishes several builds a day, and a weekly PR would be noise rather than signal; `workflow_dispatch` takes a `force` input for the exceptions. It then builds and starts the stand on the new pin and requires the AMM sentinel amendment to come up enabled at genesis, which is what proves the regenerated config was accepted rather than silently ignored, and attaches the definitions diff against the new build to the PR body — a `node-only` field there is the SDK being behind develop, reported instead of hidden
  * credentials, idempotency and the tracking-issue fallback follow release-watch exactly, including the one-notification-per-failure-streak rule
* **Cancellation no longer disappears into the autofill fee fallbacks** — `FetchCounterpartySignerCount` and `FetchLoan` wrap their client call in a broad `catch`, which is right for the case they exist for (the counterparty account or the Loan object is not there yet, and preclaim will report it) but also swallowed an `OperationCanceledException` raised from the caller's own token. Autofill then carried on and wrote a fee derived from the fallback — one signer, no loan — for a request the caller had already abandoned. Both catches now carry `when (!cancellationToken.IsCancellationRequested)`, which lets a caller's cancellation through while a client-side timeout, which does not cancel that token, still falls back as before. Covered in both directions: a cancelled token must throw and leave no `Fee` behind, an unreadable Loan object must still fall back
* **`MPTokenIssuanceSet` validation reports a malformed `Flags` as `ValidationException`** — it went through `Convert.ToUInt32`, which throws `FormatException` or `InvalidCastException` on a non-numeric value, while the `ImmutableFlags` check two lines below reports `ValidationException` like the rest of the validators. Callers catching `ValidationException` did not catch the other two
* **The conformance fixtures are re-pinned to the 3.3.0 tag** — `transactions.macro` and `LedgerFormats.h` now come from the release commit (`00a178fb`) instead of a July `develop` sha and `3.3.0-rc1`; `ledger_entries.macro` stays on `develop` (`9859e5ce`) for the reason its `.ref` already gives — `sfLEVersion` exists only there. Both macro files are byte-identical to upstream and re-verifiable with the `curl … | diff` line in each `.ref`. This is what makes the guards test against the version CI actually runs:
  * `RippledLedgerFlags.Parse` learned to read the `lsif*` values. In 3.3.0 they are no longer a `LEDGER_OBJECT(MPTokenIssuanceMutable, …)` block but plain `inline constexpr std::uint32_t` constants next to the macro list, so the flag guard would have quietly lost that enum entirely. They are reported under a synthetic `MPTokenIssuanceImmutable` object, and a parse that finds none of them now throws instead of returning a thinner table
* **Why the weekly Definitions Watch stayed green through all of this** — `definitions-watch.yml` raises a stand from `docker-compose.batchv11.yml`, i.e. the **pinned** nightly `XRPLD_VERSION`. While the pin is stale the monitor diffs `definitions.json` against a build older than the one CI runs, and reports "in sync" about the past. The pin needs to move with every stable bump, not only when a new amendment is wanted

* **Autofill covers the three remaining transactors with a special base fee** — `Transactor::calculateBaseFee` is overridden by ten transactors upstream, and the fee sugar implemented only some of them. The three that were missing all **underpay**, which is the failing direction: a fee below the required minimum is rejected with `telINSUF_FEE_P` instead of being topped up. Each is verified against the rippled source rather than inferred from the field layout:
  * **`LoanSet` no longer assumes a single counterparty signature.** The old formula was a flat `baseFee * 2`, correct only when the counterparty signs with its master key. `LoanSet::calculateBaseFee` charges one base fee per entry of `CounterpartySignature.Signers`, so a counterparty that multi-signs made the autofilled fee too low. When the signature is already attached — `LoanSigningHelper` ran first — its signers are counted directly; when it is not, which is the usual order during autofill, the counterparty's signer list size is fetched and used, matching what xrpl.js does for the same transaction. Absent signer list, or an account that does not exist yet, falls back to one signature
  * **`LoanPay` charges per five payments processed.** `LoanPay::calculateBaseFee` multiplies the *whole* Transactor cost — signatures included — by one increment per `kLoanPaymentsPerFeeIncrement` (5) payments the transaction is expected to make, capped at `kLoanMaximumPaymentsPerTransaction / 5` (20). Paying off six or more scheduled payments in one transaction therefore costs at least twice the base fee, and nothing in the SDK accounted for it. The estimate reads the `Loan` object, derives the per-payment amount as `roundPeriodicPayment(PeriodicPayment, LoanScale) + LoanServiceFee` — rounding up to whole units for XRP and MPT, to a multiple of `10^LoanScale` for IOUs, as `roundToAsset` does — and divides the transaction `Amount` by it. Every path rippled short-circuits is mirrored: `tfLoanFullPayment` and `tfLoanLatePayment` do one set of calculations, `PaymentRemaining <= 5` needs no increments, and an unreadable `Loan` object falls back to the normal cost the same way rippled leaves the error to preclaim. The asset's integrality is taken from the transaction's own `Amount`, which rippled requires to match the vault asset, so no broker/vault lookups are needed
  * **Confidential MPT transactions pay the confidential multiplier.** All five (`ConfidentialMPTSend`, `ConfidentialMPTConvert`, `ConfidentialMPTConvertBack`, `ConfidentialMPTMergeInbox`, `ConfidentialMPTClawback`) call `Transactor::calculateBaseFee` with `kConfidentialFeeMultiplier` = 9, i.e. ten base fees for a single-signed transaction, paying for the cryptographic proofs they carry. They were being autofilled at one base fee — a tenfold underpayment

* **Repeated `OnConnected` handler failures now back off** — the give-up branch added earlier is bounded by `MaxReconnectAttempts`, but only when `StopAfterMaxAttempts` is set. With it turned off there is no give-up at all, and the delay between retries never grew: this path tears the reconnect loop down and starts it again on every failure, `StopReconnectLoop` zeroes `_reconnectAttempts`, a fresh sequence zeroes it again, and `CalcBackoff` derives the delay from that counter alone. The client therefore repeated connect → handler failure → teardown at a constant `ReconnectBaseDelay` forever — a sustained connection load on exactly the node that cannot serve requests yet. `StartReconnectLoop` now takes the value to seed the counter with, and the handler-failure path seeds it from its own consecutive-failure count so the sequence keeps growing across failures. `TestRepeatedOnConnectedFailuresBackOff` pins it; reverting the fix makes that test show ~100 reconnects in 20s at a flat ~200ms interval
* **`_reconnectCts` is `volatile`** — the reconnect loop compares it by reference to decide whether it still owns the reconnect state, while `StopReconnectLoop`, `StartReconnectLoop` and `RetireCurrentSessionAndReconnectAsync` write it from other threads. A stale read could let a retired loop run one more iteration or make the owning loop stand down early. The other cross-thread fields in that class were already `volatile`
* **Lending guide corrected** — the `Loan Fields` table in `LendingProtocol-Guide` (both languages) listed four names the ledger object does not have: `Account` (the borrower is in `Borrower`), plus `Counterparty`, `PrincipalRequested` and `PaymentTotal`, which are fields of the **`LoanSet` transaction**. After `PrincipalRequested` was removed from `LOLoan` in this release the guide would have promised a property that no longer exists. Fixed, with a note pointing the three transaction fields at `LoanSet`
* **JSON serialization: the derived converter options are cached, and unknown ledger-object types no longer read as AccountRoot** — every polymorphic converter (`LOConverter`, both transaction converters, `MetaBinaryConverter`, `LedgerBinaryConverter`, `LONFTokenConverter`, `GenericStringConverter<T>`) re-enters the serializer with its own converter removed to break the recursion, and each call built that derived `JsonSerializerOptions` from scratch — an allocation, a copy of the whole converter list and a structural-equality lookup in System.Text.Json's caching-context pool, per converted value, so once per element of a page. Type metadata was not rebuilt each time: since .NET 8 System.Text.Json shares a caching context between structurally equal options instances, which is what kept the per-call copy from being far worse than it was — measured on 200 `account_objects` pages of 200 entries, 456 ms / 47 MB allocated before against 217 ms / 29 MB after. `JsonSerializerOptionsCache` builds the derived options once per (source options, converter type), keyed weakly on the source so caller-supplied options stay collectable — safe because System.Text.Json freezes an options instance on first use, so what a converter is handed can no longer change. It also drops the reliance on that context pool, which is capped at 64 entries:
  * `LOConverter.DetermineType` resolved an unrecognized `LedgerEntryType` to **`LOAccountRoot`**. `Enum.TryParse` writes `default(TEnum)` into its `out` on failure and `AccountRoot` is the zero value, overwriting the `Unknown` the variable was initialized with. A ledger object type newer than the SDK was therefore deserialized as an account root with every field silently dropped, instead of falling back to `BaseLedgerEntry` the way `LedgerEntryTypeConverter` and `NodeConverterBase` already do. Pinned from both entry points — a bare `BaseLedgerEntry` and an `account_objects` page
  * the `//todo change from class to interface and parse same as transactionResponse` on `AccountObjects.AccountObjectList` is dropped rather than implemented. The parsing half has been true since `LOConverter` was registered globally, and `BaseLedgerEntry` has to stay a concrete class precisely because it is the `Unknown` fallback — an interface would need a sentinel type, which is what `TransactionResponseUnknown` exists to be. Nothing pinned the polymorphism for the response model itself; `TestUAccountObjectsPolymorphism` now does

* **Test-side fixes** — `TestUtils.GetFreePort` never handed out a port twice within the process (the OS is free to return a just-released port, and test classes run in parallel, so two callers could get the same one and the second mock would fail to bind on its background thread, surfacing as a timeout rather than an error); `TestUChangeServerFailure` checks the port is still free right before starting the second mock, so the remaining external race fails fast with a clear message; `RippledLedgerFlags.Parse` throws on a ledger object declared twice, matching `RippledLedgerEntryFormats.Parse`; the fixture entries in the test `.csproj` use `None Update` instead of `None Include`, since the SDK's default glob already includes them

* **`TestULedgerEntryFieldsConformance` — the third conformance surface**, completing the set next to `TestUTxFormatConformance` (transaction fields) and `TestULedgerFlagsConformance` (ledger flags). `ledger_entries.macro` is the only place the protocol states which fields belong to which ledger object — `definitions.json` carries field codes and object types but not the per-object lists — and nothing checked it. A missing field produces no symptom: reading the object still succeeds and the value is silently dropped, which is how `LOAccountRoot` went without `WalletLocator`/`WalletSize` until a manual pass, and how `sfLEVersion` had to arrive through a protocol-watch notification instead of a red test:
  * `Tests/Xrpl.Tests/Fixtures/ledger_entries.macro`, vendored byte-identical and pinned by sha in the `.ref`. Pinned to a **develop** commit rather than a tag, unlike `LedgerFormats.h`: the models track develop for fields, and `sfLEVersion` exists only after 07/30/2026, so a tag would report it as a field the SDK invented
  * both directions are diffed — a field rippled declares and the model lacks, and a property the model exposes that is not a field of that object — and every ledger object must be registered against a model, so a newly added one fails the build instead of being skipped
  * rippled's four **common fields** (`LedgerIndex`, `LedgerEntryType`, `Flags`, `Sponsor` from `LedgerFormats::getCommonFields()`) are excluded on both sides, mirroring how the TxFormat guard treats `commonFields`; `[JsonIgnore]` properties (computed helpers like `DataParsed`, `MPTokenMetadataRow`) never reach the wire and are excluded too
  * verified by mutation: renaming a field's `JsonPropertyName` makes it report both halves (`Loan.Borrower … missing from LOLoan` and `LOLoan.BorrowerX … not a field of Loan`)

* **Ledger-object properties that are not protocol fields — removed** (**breaking**, no `[Obsolete]` grace period, consistent with the 10.10.0.0 removal of the inert `ConnectionOptions`). None of them could ever hold a value: rippled builds each object from a fixed `SOTemplate`, so a field outside the template cannot appear in it. Confirmed against a live node (nightly stand, 3.3.0-b1) *and* across four rippled versions — 3.2.1, 3.3.0-b1, 3.3.0-rc1 and develop — none of these exists in any of them, including the unreleased one:
  * `LOVault.DomainID` — proven with a positive control: a `VaultCreate` carrying `Data`, `AssetsMaximum` **and** `DomainID` succeeded, the first two came back on the object, `DomainID` did not, and it turned up on the linked share `MPTokenIssuance` instead — exactly what the macro comment (`no PermissionedDomainID ever (use MPTIssuance.sfDomainID)`) and `VaultCreate.cpp` (`.domainId = tx[~sfDomainID]`) describe
  * `LOLoan.PrincipalRequested` — a field of the **LoanSet transaction**, not of the object: a real loan created with `PrincipalRequested = 10000000` stores it as `PrincipalOutstanding`, and the object carries no such field
  * `LOCredential.OwnerNode` — Credential hangs in two directories and uses `IssuerNode`/`SubjectNode`. Zero-valued directory hints *are* serialized (a Loan object returns `"OwnerNode":"0"`), so its absence is real, not a default being omitted
  * `LONFTokenPage.NFTokenPage`, `LOAmm.LedgerCurrentIndex`, `LOAmm.Validated` — the last two are fields of the `amm_info` **response envelope** (`ledger_current_index`, `validated`, snake_case), not of the AMM object; `LOAmm` is only ever deserialized as a ledger object, and `amm_info` has its own `AMMInfo` model

* **`LOAmm` fixes** — two bugs the guard surfaced:
  * **`AMMAccount` never deserialized**: the AMM object's field is `Account`, and the property had no `[JsonPropertyName]`, so it silently stayed null on every AMM object ever read. Now mapped to `Account`; the property name is unchanged, so no call site breaks
  * the constructor set `LedgerEntryType = LedgerEntryType.AccountRoot` — an AMM object identified itself as an AccountRoot. Now `LedgerEntryType.AMM`

* **Fields declared by the protocol but missing from the models** — `PreviousTxnID`/`PreviousTxnLgrSeq` on `LOAmm`, `LOAmendments`, `LODirectoryNode`, `LOFeeSettings` and `LONegativeUNL`. Both are `SoeOptional` on these objects upstream; without them the transaction that last touched the object could not be read through the typed API

* **`sfLEVersion` — the Vault ledger entry's schema version** ([rippled #7817](https://github.com/XRPLF/rippled/pull/7817), merged into `develop` 07/30/2026, reported by protocol-watch). `UInt8` nth 6, `SoeDefault` on `ltVAULT`: it marks which accounting scheme a vault follows. Vaults created before cash-basis accounting was activated carry no `LEVersion` at all, and rippled resolves that absence as version 0 rather than an error — so an absent value is meaningful, not missing data:
  * `definitions.json` + the generated `Field.Uint8` entry. **Both are required**: `definitions.json` is not read at runtime, it is the input to `Tools/GenerateEnums`, so a field added there alone travels nowhere. `TestULEVersion_BinaryRoundTrip` is what proves the round trip actually works rather than that the JSON was edited
  * `LOVault.LEVersion` (`uint?`, matching the other UInt8 fields of that object) plus a `VaultVersion` enum naming the two values the protocol defines so far (`Legacy` = 0, `CashBasis` = 1)
  * `TestULOVault_LEVersion_Deserialize` covers both shapes — the field present, and a legacy vault without it deserializing to `null`
  * `Xrpl.BinaryCodec` bumped to **10.11.0.0**, aligned with `Xrpl` rather than to its own next minor (10.10.0.0): the codec ships the field, so the two move together and a consumer can read one version number off both. 10.10.x is simply skipped — the codec's last published version is 10.9.0, so no number is being reused. `Xrpl.AddressCodec` and `Xrpl.Keypairs` are untouched and keep 10.9.0.0

* **Ledger-object flags the protocol declares but the models never named** — an unnamed bit still arrives in the model as a number, so reading the object kept working and only the consumer's ability to test it by name was lost. That is why these went unnoticed; a field-by-field diff of rippled `LedgerFormats.h` (tag `3.3.0-rc1`) against every flag enum found four gaps:
  * `MPTokenIssuanceFlags` + **`MPTCanHoldConfidentialBalance`** (0x80) — introduced by ConfidentialTransfer. The rest of the amendment was already complete (transactions 85–89, `IssuerEncryptionKey`/`AuditorEncryptionKey`, `ConfidentialOutstandingAmount`); only the flag had no name. Value confirmed against a live node: `MPTokenIssuanceSet` with `Flags = tfMPTSetCanHoldConfidentialBalance` moves the issuance from `Flags = 0` to `Flags = 128`
  * `MPTokenFlags` + **`lsfMPTAMM`** (0x4) — a much older gap: the flag is present as far back as 3.2.1. `AMMCreate` sets it together with `lsfMPTAuthorized` to implicitly authorize an MPT asset for the AMM pseudo-account
  * **`LOLoan.Flags`** — the Loan ledger object had no `Flags` property at all (and `BaseLedgerEntry` has none either), so `lsfLoanDefault`/`lsfLoanImpaired`/`lsfLoanOverpayment` were unreadable through the typed model: the default and impairment state of a loan could not be observed at all. Added as a typed `LoanFlags?` together with the enum
  * new `SignerListFlags` (`lsfOneOwnerCount`) and `DirectoryNodeFlags` (`lsfNFTokenBuyOffers`/`lsfNFTokenSellOffers`) — both objects expose `Flags` as a raw `uint` and **keep doing so** (changing the property type would be breaking); the enums give consumers named constants to test bits against instead of magic numbers. The `LODirectoryNode.Flags` comment claiming "the protocol defines no flags for DirectoryNode objects" was false and is corrected

* **`TestULedgerFlagsConformance` — the guard that would have caught all of the above** — `LedgerFormats.h` is the only place the protocol states which `lsf` flags belong to which ledger object (`definitions.json` carries field codes and entry types, but no flag values). Nothing checked it, which is how `lsfMPTAMM` survived several releases. The new test is the ledger-side counterpart of `TestUTxFormatConformance`:
  * `Tests/Xrpl.Tests/Fixtures/LedgerFormats.h` is vendored byte-identical and pinned by sha in `LedgerFormats.h.ref`, verifiable with a plain `curl … | diff`. Pinned rather than live for the same reason as `transactions.macro`: upstream drift is protocol-watch's job, and a network-backed test would go red on Ripple's release schedule instead of ours
  * `RippledLedgerFlags` parses the `LEDGER_OBJECT`/`LSF_FLAG` macro text and fails loudly — an unknown `LSF_FLAG*` variant or a parse yielding fewer than 10 objects / 50 flags throws rather than leaving the test green on an empty table
  * the test diffs **both directions** (a flag rippled declares and the enum lacks, a flag the enum has and rippled does not) and requires every flagged object to be registered against a model enum, so a newly added ledger object fails the build instead of being skipped. `tf*` members sharing an enum with ledger flags (`OfferFlags.tfInnerBatchTxn`) and zero-valued members are excluded by rule
  * name matching normalizes the `lsf`/`lsif`/`tif` prefixes, so `lsfMPTLocked` ≡ `MPTLocked` and rippled's `lsifMPTCanLock` ≡ the SDK's `tifMPTCanLock` (rippled itself aliases `tifX = lsifX` in `TxFlags.h`)
  * verified by mutation, not just by passing: a wrong value, a removed flag and an unregistered object each make it fail with a readable message
  * `protocol-watch` now watches `include/xrpl/protocol/LedgerFormats.h` as well. A pinned fixture cannot notice upstream moving — that signal is the watcher's job, and the header was missing from its list (which is the other half of why `lsfMPTAMM` went unnoticed for so long). The first run after this change reports the header as changed once, then carries it in the baseline like the rest

* **DynamicMPT (XLS-94) integration coverage** — the immutability fields existed on the models but had never been exercised against a node. `AmendmentGuard` gains the `DynamicMPT` id (it matches what `generate-amendments.sh` already writes into the nightly stand's `[amendments]`), and `TestIDynamicMPT` covers the amendment end to end, each test reading the result back from the ledger object rather than trusting `EngineResult`:
  * `ImmutableFlags` set at `MPTokenIssuanceCreate` reach `LOMPTokenIssuance` unchanged
  * `MPTokenIssuanceSet` mutates `TransferFee` and `MPTokenMetadata` on an issuance that froze neither, and leaves `ImmutableFlags` unset (`doApply` only ORs that field when the transaction carries it)
  * `tfMPTSetCanLock` raises `lsfMPTCanLock` on an issuance created **without** that capability
  * a mutation of a frozen field is rejected with `tecNO_PERMISSION` and leaves the metadata untouched, whether the freeze came from the create or from a later set
  * scenarios were derived from the transactor (`src/libxrpl/tx/transactors/token/MPTokenIssuanceSet.cpp` @ `3.3.0`), not from the docs — hence `tfMPTCanTransfer` at creation in the fee test: `preclaim` requires `lsfMPTCanTransfer` to be **already** set, and enabling it in the same transaction does not satisfy the rule
  * amendment-gated, so it skips on the CI stand (rippled 3.2.x has `DynamicMPT` as `Supported::No`) and runs for real on the nightly stand

* **An exception from an `OnConnected` handler no longer kills the client forever** — `Connection.OnceOpen` caught anything thrown by a consumer `OnConnected` handler and called `Disconnect()`, i.e. the *user* disconnect path: it set `_permanentlyDisconnected = true` and called `ClearReconnectState()`. After that the client was dead — the reconnect loop was never restarted, no new socket was ever opened, `OnConnected` never fired again, and every later request threw `NotConnectedException("Client has been disconnected. Call Connect() to reconnect.")`. Nothing was logged and nothing was raised, so from the outside the client just went quiet:
  * **The trigger is the most ordinary event there is — a node restart.** `OnConnected` is the natural place to restore subscriptions, because the SDK does not restore them after a reconnect. A restarting node accepts TCP seconds before it starts answering requests, so the first `subscribe` after the reconnect runs into `RequestTimeout` (40 s) and throws. A consumer that lets the exception out — the reasonable "fail loudly, let the SDK reconnect" reaction — got the opposite: a silent, permanent death. Observed in production on a fleet of bots, each wedged for four hours after a node upgrade, one of them dying 69 seconds before the node came back
  * A failing handler is now treated as what it is — a **connection** failure, not a user disconnect. The socket is torn down and the regular reconnect loop takes over with its usual exponential backoff, exactly as for a transport failure. The permanent-disconnect flag is never set on this path
  * **A permanently broken handler cannot spin forever.** `OnceOpen` clears the reconnect state before invoking the handler, so the loop's own attempt counter resets on every successful TCP connect and could never converge. Consecutive handler failures are therefore counted separately (`_connectHandlerFailures`, reset on a successful handler run, on `Connect()` and on `ChangeServer()`); once they reach `MaxReconnectAttempts` with `StopAfterMaxAttempts` set, the client gives up deliberately — an immediate, actionable `NotConnectedException` instead of a silent five-minute wait — and `Connect()` clears the counter so recovery stays possible. With `StopAfterMaxAttempts = false` it keeps retrying, which is what that option asks for
  * **The cause is now observable.** The exception is surfaced through `OnError` with `errorMessage = "connectHandlerError"` (the same shape already used for stream-handler failures) and through `OnConnectionStatus` — previously the reason the client died was reported nowhere at all
  * `TestUOnConnectedHandlerFailure` pins all four properties against the mock rippled: a transient failure recovers and the client is usable again, the failure is reported through `OnError`, and a permanently failing handler stops instead of looping
* **`ChangeServer` to a server that is not up leaves the client reconnecting instead of dead** — a second wedge of the same family, found while exercising the fix above through the Blazor demo (switch the network selector to a node that is down). `ChangeServer` set the *global* `_isIntentionalDisconnect` flag to filter late callbacks from the socket it was retiring, and that flag was only ever reset in `OnceOpen`. If the new server never came up, `OnceOpen` never ran: `OnConnectionFailed` then read the failure of the **new** connection as a user disconnect, reported `"Connection closed permanently."`, started no reconnect loop, and every later call — including `ChangeServer` itself — failed with the misleading `"No connection attempt in progress. Call Connect() first."` Starting the server afterwards changed nothing; the client was dead. Late callbacks are now filtered purely by the per-socket tracking that was already in place (`_userInitiatedSockets` plus the socket's own flag), exactly as the ping-timeout/network-drop path has always done — its code even carries a comment warning against setting the global flag for this reason. The flag is additionally cleared on entry, so a `ChangeServer` after a user `Disconnect()` is not suppressed by the leftover either. `TestUChangeServerFailure` pins both cases: the client reaches the new server once it appears, with and without a preceding `Disconnect()`
* **The reconnect loop no longer writes to a reconnect session it no longer owns** — `StopReconnectLoop()` cancels the loop's token without awaiting the loop, so a retired loop could still reach its body or its tail after a replacement had been installed and clear the *live* loop's `_reconnectMode`, reset its `_reconnectAttempts` or dispose its `_reconnectCts`. Pre-existing (`RetireCurrentSessionAndReconnectAsync` has always retired loops this way), but the handler-failure path above makes it far more reachable, so `ReconnectLoopAsync` now takes the `CancellationTokenSource` it owns and touches shared state only while that source is still the active one
* **`WaitForConnectionAsync` now rechecks the permanent-disconnect flag on every iteration**, not only once on entry. A caller already blocked there when the client is disconnected — by `Disconnect()` from another thread, or by the give-up path above — used to sit out the whole `ConnectionAcquisitionTimeout` (default five minutes) and then receive a generic `TimeoutException`. It now returns the actual reason immediately as a `NotConnectedException`
* **`WebSocketClient.SendMessageAsync` no longer swallows send failures silently** — it is `async void` and is invoked without `await` from `Connection.WebsocketSendAsync`, so a failed send could be reported to nobody: the pending request simply sat there until its 40-second `RequestTimeout` expired. The socket's error callback (previously dead code — nothing ever invoked or wired it) now carries the exception to `Connection.OnError` with `errorMessage = "socketSendError"`. Report-only: a failed send does not by itself mean the connection is gone, so this path never triggers a reconnect and the request is still bounded by `RequestTimeout` — but the cause is no longer invisible during diagnosis

## 10.10.0.0 07/29/2026
* **`ConnectionOptions.authorization` did nothing — now it does** — the option was public on `XrplClient.ClientOptions` since the xrpl.js port, but `Connection.CreateWebSocket` was a block of commented-out JS pseudocode ending in `WebSocketClient.Create(url); // todo add options`, and `WebSocketClient` had no parameter to receive them. Nothing the caller set on `authorization`, `headers`, `proxy`, `trustedCertificates`, `key`, `passphrase` or `certificate` ever reached the socket:
  * `authorization` now produces `Authorization: Basic base64(value)` on the WebSocket upgrade handshake, matching xrpl.js `createWebSocket` — the value is the raw `user:password` pair, the SDK does the base64
  * `headers` are put on the handshake as-is; the type changed from `Dictionary<string, object>` to `Dictionary<string, string>` to match xrpl.js and drop the `ToString()` ambiguity (**source-breaking**, but the property was inert, so no working code can depend on it)
  * both are skipped under WebAssembly — the browser WebSocket API cannot set request headers, so `ClientWebSocket.Options.SetRequestHeader` is guarded by `OperatingSystem.IsBrowser()` the same way `KeepAliveInterval` already was
  * `proxy`, `proxyAuthorization`, `trustedCertificates`, `key`, `passphrase`, `certificate` and the unused `trace`/`Trace` pair are **removed** rather than implemented (**breaking**, no `[Obsolete]` grace period — consistent with the 10.9.0.0 hex-helper removals): current xrpl.js has dropped these options too, they cannot be honored uniformly across `ClientWebSocket` targets, and nothing in the solution ever read them. A property that silently does nothing is worse than one that does not compile
  * **Scope note:** rippled does *not* check Basic auth on the ws/wss handshake — `authorized()` is called only from the plain-HTTP `onRequest()` path, while `onHandoff()` upgrades WebSockets without it. A port stanza's `user`/`password` therefore only guards HTTP JSON-RPC. `authorization` is for reaching a node behind a reverse proxy or a provider that requires Basic auth

* **`AdminUser`/`AdminPassword` — admin commands over WebSocket** — the mechanism rippled actually accepts for ws/wss: `admin_user`/`admin_password` travel *inside the request JSON*, not in a header. Without them, a port that sets `admin_user`/`admin_password` rejects `ledger_accept`, `stop`, `connect` and friends outright — `forbidden` / `Bad credentials.` — regardless of the client's IP, because `requestRole` returns `Role::FORBID` rather than demoting the client to guest. Both must be set for either to be sent, mirroring rippled's own check (a matching `admin` net **and** correct credentials)
  * injected into the serialized request rather than into the request object, so the credentials never reach the `TimeoutException` message that consumers log — `TestAdminPasswordIsNotLeakedIntoTimeoutMessage` pins that
  * `RequestManager.CreateRequest`/`CreateGRequest` take the credentials as a trailing optional parameter, so existing positional call sites are unaffected

* **Coverage** — `TestUAuthorization` asserts against the raw HTTP upgrade text captured by a loopback socket server: Basic header present and correctly encoded, custom headers present, and no `Authorization` header when the option is unset. `TestIAdminCredentials` runs against a new `[port_ws_admin_auth]` stanza on the standalone stand (port 6007, `admin_user`/`admin_password` set) and checks both directions — `ledger_accept` rejected with `forbidden` / `Bad credentials.` without credentials, accepted with them. The port is separate from `port_ws_admin` so the rest of the integration suite is untouched

* **Transaction fields declared by the protocol but missing from the models** — `TxFormat` listed them and the binary codec knew them, so the values travelled fine through `Dictionary<string, object>`, but the typed models had no property: reading silently dropped them and the typed API could not set them at all. A field-level diff of `TxFormat` against the transaction models found four such names; the earlier 10.7.0.0 completeness pass had closed the ledger-object side (`LOAccountRoot.WalletLocator`/`WalletSize`) but not the transaction side:
  * `TransactionRequest`/`TransactionResponse` + **`Delegate`** and **`OperationLimit`** — both are rippled *common* fields (`TxFormats.cpp` `commonFields`), valid on every transaction type, so they belong on the shared base rather than on individual transactions. `Delegate` identifies a transaction submitted under DelegateSet permissions (previously readable only from raw JSON, though `BatchUtils` already honored it when collecting required batch signers). `OperationLimit` is inert on XRPL but is the marker Xahau's Burn-2-Mint reads on a burn — consumers no longer need to build the burn as a dictionary to get it onto the wire, nor read raw JSON to tell a burn from a plain `AccountSet`
  * `AccountSet`/`AccountSetResponse` + **`WalletLocator`** and **`WalletSize`** — both still stand in rippled's AccountSet format (`transactions.macro`). `WalletSize` is legacy and not acted on by the transactor; it is exposed so a transaction carrying it survives a round trip
  * `ValidateBaseTransaction` type-checks the two new common fields, as it already does for every other common field; `ValidateAccountSet` does the same for the two new AccountSet fields — `WalletSize` as a UInt32, and `WalletLocator` as a 256-bit hex value, which is the rule `sfWalletLocator`'s `Hash256` type implies and the one the SignerListSet validator already applies to a `SignerEntry`'s WalletLocator
  * **`Target` deliberately not added** — it is not a protocol field: `sfTarget` is retired (AccountID nth 7 is marked unused in `sfields.macro`, and the name is absent from `definitions.json`), and since the TicketBatch amendment rippled's TicketCreate carries only `sfTicketCount`. The stale `Target`/`Expiration` entries were removed from `TicketCreate` in `TxFormat`; `Field.Target` stays in the codec so historical blobs still decode
  * `TestUTransactionProtocolFields` pins the whole cycle — deserialization, `ToJson`/`ToDictionary` round trip, typed-vs-dictionary signing parity byte for byte, and, as the regression guard for touching the common base, blobs of transactions that set none of the new fields against signatures captured from 10.9.1.0

* **`TxFormat` brought into full conformance with rippled, and held there** — the table is inert at runtime (`TxFormat.Validate` is not on the signing path; the codec serializes from `definitions.json`), so wrong entries produced no symptom and nothing in the suite noticed. A field-by-field diff against rippled `transactions.macro` found seven wrong formats out of 82; all are corrected and the table now matches upstream exactly:
  * `CheckCreate`, `CheckCash`, `CheckCancel` — all three were a verbatim copy of the `PaymentChannelClaim` entry above them (`Channel`/`Amount`/`Balance`/`Signature`/`PublicKey`). Now `CheckCreate` = `Destination`+`SendMax` required, `Expiration`/`DestinationTag`/`InvoiceID` optional; `CheckCash` = `CheckID` required, `Amount`/`DeliverMin` optional; `CheckCancel` = `CheckID` required
  * `NFTokenMint` — was missing `Amount`/`Destination`/`Expiration`; the NFTokenMintOffer fields reached the *model* in 10.7.0.0 but the format never followed, so the two had silently drifted apart from each other
  * `OracleSet` — dropped `BaseAsset`/`QuoteAsset`/`AssetPrice`/`Scale`, and `SignerListSet` dropped `WalletLocator`: in both cases these are members of a nested object (`PriceDataSeries` entries, `SignerEntry`) that had been hoisted to the top level
  * `VaultCreate` — dropped `Amount`, which is not a field of that transaction
  * **`TestUTxFormatConformance`** now diffs every one of the 82 formats against a vendored, ref-pinned copy of `transactions.macro` (`Tests/Xrpl.Tests/Fixtures/`) and reports each divergence by name. Pinned rather than live on purpose: upstream drift is already protocol-watch's job (`transactions.macro` is in its watch list), and a network-backed test would go red on Ripple's release schedule instead of ours. The parser fails loudly on an unknown `Soe*` keyword or a short parse, so a macro-layout change cannot turn the guard green on an empty table
* **Fix `CheckCreate.InvoiceID`: `uint?` → `string`** (**breaking signature change**, though nothing could have depended on it) — `sfInvoiceID` is a `Hash256`, `Payment.InvoiceID` was already `string`, and `ValidateCheckCreate` already rejected anything but a string. The typed property was `uint?`, so every non-null value threw at signing time (``Can't decode `InvoiceID` from `123` ``): the field was unusable through the typed API in any release that had it. Found while writing the integration coverage for the corrected `CheckCreate` format
* **Integration coverage for the corrected field sets** (`TestIProtocolFieldSets`, standalone stand) — `TxFormat` itself cannot be exercised end-to-end, so these pin the claim underneath it against a real node: `CheckCreate` carrying `Expiration`/`DestinationTag`/`InvoiceID` lands and the `Check` object reads them back; `CheckCash` settles through the previously untested `DeliverMin` branch; `NFTokenMint` with `Amount`/`Destination`/`Expiration` creates the mint-time sell offer; and an `AccountSet` with `WalletLocator`/`WalletSize`/`OperationLimit` survives a full ledger round trip back into the typed `AccountSetResponse` — the end-to-end proof for the model work above. `Delegate` is covered by `TestDelegatedPayment_DelegateFieldSurvivesTheLedgerRoundTrip` (amendment-gated on `PermissionDelegationV1_1`, so it runs on the nightly stand): the owner grants the Payment permission, the delegate signs a Payment whose `Account` is the owner and whose `Delegate` is itself — without the field rippled would reject the signature outright — and the transaction reads back into the typed model with `Delegate` set, both directly and through `ITransactionCommon`

* **Integration suite no longer reaches outside the standalone stand** — two places still went over the public internet, so a green build depended on third-party availability:
  * `TestIConnectionStates` (7 tests) pointed at the public testnet and devnet. Nothing in them is specific to a public network — every assertion is about the client's own state machine — so they now run against the local node. The bogus-hostname case that tested reconnect exhaustion used a DNS lookup; it now uses a closed loopback port, which refuses immediately and involves no resolver. Fixed `Task.Delay` sleeps were the other half of the flakiness (one of these tests failed a full run and passed on retry) and are replaced by waiting for the expected state with a timeout: the class went from ~40 s of sleeping to sub-second assertions
  * the x402 live t54 interop tests need the public testnet faucet *and* a hosted third-party facilitator. They are now `[TestCategory("Live")]` and excluded from CI (`--filter "TestI&TestCategory!=Live"`), leaving the six hermetic x402 E2E tests in the run. Invoke them deliberately with `--filter "TestCategory=Live"`

* Review pass (PR [#68](https://github.com/StaticBit-io/XrplCSharp/pull/68)):
  * **`ValidateCheckCreate` now enforces `InvoiceID` as a Hash256**, not merely as a string. `sfInvoiceID` is a 256-bit hash and this same release added exactly that rule for `WalletLocator` in `AccountSet` and `SignerListSet`, so `CheckCreate` was the odd one out: a malformed value passed validation and only blew up later inside the codec, reporting an encoding error instead of a `ValidationException`. The exception message is unchanged (`CheckCreate: invalid InvoiceID`)
  * **the CI stand publishes every port on loopback only**, matching the nightly stand. The review flagged the new credential-protected ws port (6007) for being bound to every interface, but that was the least exposed of the four: `rippled.cfg` sets `admin = 0.0.0.0` on every stanza, so 5005/5006/6006 hand the admin role — `stop`, `connect`, `feature`, `validation_seed`, i.e. node control — to anyone who can reach them, and 6007 is the only one that asks for credentials at all. Nothing needed the wider binding: tests connect over localhost and the ledger-acceptor reaches the node through the compose network. Worth doing even though the node is a throwaway genesis container, because a host firewall does not cover this — Docker's DNAT rules sit ahead of the chains `ufw` manages
  * `TestIConnectionStates` — both reconnect-exhaustion tests discarded the `Task.WhenAny` winner, so a run where the terminal event never arrived proceeded after the 30 s timeout and could still pass. They now assert the event task won, and that at least one reconnect was attempted
  * `TestIProtocolFieldSets` sets `Expiration` on the mint-time NFT offer but never checked it read back; asserted now, closing the last unverified field of the corrected `NFTokenMint` set
  * test-only tidying: the parse-floor literal is shared instead of duplicated (`RippledTransactionFormats.MinimumExpectedTransactions`), the common-field set both conformance surfaces subtract now comes from one helper (`RippledTransactionFormats.CommonFields`), and a redundant `Link` on the vendored fixture is dropped

## 10.9.1.0 07/27/2026
* **Fix `account_tx` losing the payment amount and, on API v1, the whole transaction** — a silent regression introduced by the 10.3.0.0 `Newtonsoft.Json` → `System.Text.Json` migration; affects every release from 10.3.0.0 on:
  * `Payment`/`PaymentResponse.DeliverMax` — the private set-only alias that maps API v2's `DeliverMax` onto `Amount` was carried over from Newtonsoft (which deserializes attributed non-public members) but `System.Text.Json` skips non-public members without `[JsonInclude]`. Every Payment read through `AccountTransactions`, `TxV2` or the transaction streams came back with `Amount = null` — no exception, no diagnostic. `Tx()` was unaffected because it pins `ApiVersion = 1`, and `meta.delivered_amount` kept parsing correctly, which is why the loss went unnoticed. The alias stays set-only, so `DeliverMax` is still never serialized back out
  * `TransactionSummary` now accepts both envelopes: rippled wraps the transaction in `tx_json` under API v2 and in `tx` under API v1 — only `tx_json` was mapped, so `Transaction` was `null` for the entire history whenever `ApiVersion = 1` was requested. `Hash` and `LedgerIndex` live inside the envelope under API v1 and fall back to it accordingly (previously `Hash` came back empty, breaking hash-based lookups over the returned list)
  * Regression suite `TestUAccountTransactionsEnvelope` pins both wire shapes against trimmed captures of real testnet responses — XRP and issued-currency `DeliverMax`, both envelopes, and the guarantee that `DeliverMax` never reaches outgoing JSON

* **`GetDomainAccess` sugar helper** — client-side implementation of the `domain_access` check proposed in [XRPLF/rippled#7743](https://github.com/XRPLF/rippled/issues/7743): answers whether an account can use a permissioned domain (permissioned DEX, vaults) and why not. One `ledger_entry` domain lookup plus up to 10 parallel keylet `ledger_entry` credential lookups, all pinned to the same validated ledger; result mirrors the proposed API (`HasAccess` + `InvalidCredentials` with `Accepted`/`Expired` diagnostics, empty list = no matching credential). Semantics match rippled `credentials::validDomain`/`checkExpired`: lsfAccepted required, expired only when close time is strictly past `Expiration`, no owner shortcut, client-side expiry check (rippled deletes expired credentials lazily)

## 10.9.0.0 07/16/2026
* **Unified hex helpers ([#40](https://github.com/StaticBit-io/XrplCSharp/issues/40))** — seven overlapping implementations consolidated into two canonical utilities; **breaking removals** (no `[Obsolete]` grace period):
  * Canonical byte-level pair: `Xrpl.AddressCodec.Utils.ToHex(byte[])` / `FromHex(string)` (renamed from `FromBytesToHex`/`FromHexToBytes`); canonical string-level: `Xrpl.Utils.StringConversion` (+`Xrpl.Models.Utils.HexStringHelper` for validated/padded VL fields)
  * Removed: the global-namespace `ExtensionHelpers` class from `Xrpl.AddressCodec` (leaked `ToHex`/`FromHex` into every consumer's scope), the byte-identical `Xrpl.Client.Extensions.ExtensionHelpers` duplicate (the CS0121 ambiguity trap with `StringConversion`), dead internal copies in `Xrpl.Keypairs`/`Xrpl.BinaryCodec`
  * **Hex case convention: UPPERCASE everywhere the SDK emits hex in JSON** — matching what rippled returns, so SDK-generated hex compares `Ordinal`-equal against node output. Affected outputs: `ConvertStringToHex`, `CurrencyToHex` (Oracle nonstandard currency codes), Oracle `Provider`/`AssetClass`/`URI` (Blob fields per rippled `strHex`), cross-chain payment memos. `AssetPrice` keeps rippled's lowercase UInt64 emission. **Transaction bytes, signatures and hashes are unchanged** — hex decoding is case-insensitive on both sides
  * `HexStringHelper.FromHex` gains `trimTrailingNulls` (default `true`; `FromHexString` passes `false` so variable-length fields round-trip bytes exactly)
  * Fix `IsHexCurrencyCode`: the regex lacked `^…$` anchors — any longer string containing 40 consecutive hex chars passed as a currency code
  * Pinning suite `TestUHexHelpers` locks the unified behavior (case, null-trim, anchoring, round-trips)

## 10.8.0.0 07/14/2026
* **Unified signing & submission for sponsored transactions ([#43](https://github.com/StaticBit-io/XrplCSharp/issues/43))** — the standard `Sign`/`SubmitAndWait` now handle XLS-68 end-to-end, no helper choice required:
  * `Sign` routes by role: a wallet matching `tx.Sponsor` produces the sponsor co-signature; the submitter path preserves an existing `SponsorSignature` and guards against a `SigningPubKey` mismatch. `multisign: true` is untouched — Signer entries are section-agnostic per rippled `STTx::checkMultiSign` (identical preimage for `tx.Signers` and `SponsorSignature.Signers`), so the role is decided at composition time
  * `SignatureComposer.ComposeSignatures` (offline, explicit sponsor signers) and `client.ComposeSignatures` (ledger-driven SignerList routing with ambiguity/unknown-signer errors) assemble a fully signed transaction from partially signed blobs
  * Smart `SubmitAndWait`: a sponsor wallet finalizes a sponsee-signed transaction (compose, not re-sign) and fails fast when the main signature is missing; a sponsee submitting without `SponsorSignature` triggers a one-RPC pre-check of the Sponsorship require-sign flags (`sponsorPreCheck: false` to skip)
  * `client.ComposeSignatures` validates SignerList quorum by weights for both sections — readable client-side error instead of `tefBAD_QUORUM`
  * `SubmitAndWaitSponsored(tx, sponseeWallet, sponsorWallet)` — the both-keys-local flow in one call
  * `Sign` also routes the LoanSet borrower automatically: a wallet matching `tx.Counterparty` produces `CounterpartySignature` (XLS-66) — all three co-signing mechanisms (Batch/Sponsor/Loan) now share the no-helper-choice entry point
  * New `SignatureObject` model (shared shape of `SponsorSignature`/`CounterpartySignature`/`BatchSigner`); `LOSponsorship` gains `Flags` + `SponsorshipFlags`
  * Full live signing matrix (`TestISponsorshipSigningMatrix`): single/multisig on each side in every combination, ledger-routed composition, quorum and ambiguous-signer fail-fast, RegularKey submitter — the matrix surfaced and fixed a real preimage nuance: the multisig preimage includes the outer `SigningPubKey`, so sponsor-side signers of a single-main sponsored tx must sign over the submitter's pubkey (`SignMulti` now derives the context from the tx shape)
  * Wire-format safety: pre-refactor outputs pinned byte-level with fixed seeds (`TestUSigningPinned`); all unified flows produce byte-identical blobs; full integration suite 247/247 on the nightly stand with zero skips
* **Batch × co-signing interplay** (verified against rippled `Batch::preflight`): required batch signers now include the inner initiator (Delegate-aware), the inner `Counterparty` and the inner `Sponsor` carrying a `SponsorSignature` marker — so sponsors/borrowers of inner transactions authorize as batch signers through the same standard `Sign`; the sponsor of the OUTER batch (`spfSponsorFee`) is routed to a regular `SponsorSignature` co-signature; `ValidateBatch` enforces the new rules (no `spfSponsorReserve` on the outer, no fee sponsorship on inners, no signature material inside inner co-signature markers); live tests: a reserve-sponsored inner TrustSet lands with `HighSponsor`/`LowSponsor` set, a fee-sponsored outer batch passes co-signed, and a sponsor authorizing THROUGH ITS SIGNERLIST lands as a nested-multisig `BatchSigner.Signers` entry (the sponsor-role counterpart of the initiator-role `TestBatchMultiAccountsWithInnerMultiSign` coverage); `ValidateBatch` also rejects Loan/Vault inner transactions client-side (rippled `kDisabledTxTypes` → `temINVALID_INNER_BATCH`) — LoanSet co-signing cannot ride inside a Batch by protocol design
* Fixes accumulated since 10.7.0: TxFormat interface parity for `AMMDeposit.TradingFee`, `Uint64.FromJson` TryGetValue parsing, MPT validators mirror rippled preflight (`MutableFlags` masks, `TransferFee` vs confidential-balances rule), `LONFTokenPage.NextPageMin` doc, gateway_balances integration test rebuilt on the standalone node
* Release-review pass (PR #48): `SignMulti` preserves the submitter's `SigningPubKey` for LoanSet `Counterparty` multisign parts (the XLS-66 mirror of the sponsor preimage rule); smart `SubmitAndWait` recognizes a multisigned main signature (`Signers`) and skips autofill whenever any signature material is present (a co-signature freezes the body); `SignatureObject` enforces the two protocol shapes (single vs multisig, no empty/mixed forms) and `Combine` rejects structurally unsigned material; `DomainID` validation on MPT issuance transactions (64-char hex; non-zero + `tfMPTRequireAuth` required on Create, zero legal on Set as domain clear — per rippled preflight); `Xrpl.BinaryCodec` package version bumped to 10.8.0 (the codec changed since 10.7.0); Sponsorship guide corrects `SponsorshipTransfer` actors (Create/Reassign are submitted by the sponsee) and documents the sponsee-side `SponsorshipSet` deletion via `CounterpartySponsor`; ConfidentialMPT guide describes the integration test accurately (plain issuance, generic `tem`/`tec` assertion); protocol-watch workflow fails closed on a corrupted baseline, marks removed upstream files and skips duplicate notifications via a `head_sha` marker

## 10.7.0.0 07/13/2026
* Protocol-completeness pass driven by a field-level diff against rippled `develop` (`server_definitions` @ `8306ac77`):
  * `definitions.json`: add `HighSponsor`/`LowSponsor` (XLS-68 RippleState reserve sponsors); fix `isVLEncoded` on `Sponsor`/`Sponsee`/`CounterpartySponsor` (AccountID fields are VL-encoded); align `Generic` attributes with the node
  * Transaction models: `NFTokenMint` + `Amount`/`Destination`/`Expiration` (NFTokenMintOffer); `MPTokenIssuanceSet` + `MutableFlags`/`TransferFee`/`MPTokenMetadata`/`DomainID`/`IssuerEncryptionKey`/`AuditorEncryptionKey`; `MPTokenIssuanceCreate` + `MutableFlags`/`DomainID`; `AMMDeposit` + `TradingFee`; `LedgerStateFix` + `BookDirectory`; `VaultDelete` + `MemoData`; `SetFee` + XRPFees drops fields
  * Ledger objects: `LODirectoryNode` + `DomainID`/`ExchangeRate`/`NFTokenID`/`TakerPaysMPT`/`TakerGetsMPT`; `LORippleState` + `HighSponsor`/`LowSponsor`; `LOAccountRoot` + `FirstNFTokenSequence`/`WalletLocator`/`WalletSize`; plus `LOAmm`, `LOEscrow`, `LOPayChannel`, `LOSignerList`, `LOOracle` (`OracleDocumentID`), `LONFTokenPage`, `LOFeeSettings`, `LODelegate` field gaps
  * TxFormat: entries for all four MPT transactions
* Fix `Validation.Validate` dispatch: `NFTokenModify` was routed to `ValidateNFTokenMint` (a valid Modify without `NFTokenTaxon` was rejected); now calls `ValidateNFTokenModify`
* Fix `LOSignerList.SignerListId` never being populated: the property lacked a `JsonPropertyName` attribute and its casing did not match rippled's `SignerListID`
* Review pass (PR #34): TxFormat corrections — the entry labeled `UNLModify` actually held SetFee's legacy format; relabeled to `SetFee` (all fee fields optional per rippled `ttFEE`, + XRPFees drops fields), added the real `UNLModify` and the missing `EnableAmendment` entries; `AMMDeposit` + optional `TradingFee`, `VaultDelete` + optional `MemoData` (both verified against rippled develop `transactions.macro`); `MPTokenIssuanceSet` gains the `MPTokenMetadataRow`/`Metadata` (XLS-89) convenience accessors for parity with `MPTokenIssuanceCreate`
* Fix binary-codec JSON **encode** of UInt64 fields losing field context: a digit-only string for a hex-semantics field (e.g. `OwnerNode: "0000000000000012"`) was parsed as decimal, silently corrupting the value on round-trip. `Uint64.FromJson` now receives the field's `kSmdBaseTen` context (decimal for the five base-ten fields, strict hex otherwise) — the decode-side counterpart shipped in 10.6.0
* `Autofill` fee: account for sponsor multisig per rippled `Transactor::calculateBaseFee` — each signer nested in `SponsorSignature.Signers` adds one base fee (a single-signed `SponsorSignature` adds nothing)
* `ValidateAccountSet`: `SetFlag`/`ClearFlag` asf-range checks extracted into a shared helper
* Unit tests pinning the new fields (binary round-trips) and the dispatch fix; full integration suite (238 tests) green against xrpld `8306ac77` with all amendments active

## 10.6.0.0 07/10/2026
* **Sponsored Fees & Reserves (XLS-68, `Sponsor` amendment)** — merged into rippled `develop` on 07/10/2026 ([rippled #7350](https://github.com/XRPLF/rippled/pull/7350)):
  * New transaction models `SponsorshipSet` (91) and `SponsorshipTransfer` (90) with tf-flag enums per rippled `TxFlags.h`; `LOSponsorship` ledger object (0x90)
  * Common transaction fields `Sponsor` and `SponsorFlags` (`SponsorCoverage`: `spfSponsorFee` = 1, `spfSponsorReserve` = 2) on all transactions
  * Sponsor co-signing: `SponsorSigningHelper` (V1 automatic / V2 parallel combine / V3 sequential) and `XrplWallet.SignAsSponsor` — `SponsorSignature` is an inner not-signing STObject over the same preimage as the main signature, mirroring the LoanSet counterparty pattern
* **ConfidentialTransfer** — five transaction models: `ConfidentialMPTConvert` (85), `ConfidentialMPTMergeInbox` (86), `ConfidentialMPTConvertBack` (87), `ConfidentialMPTSend` (88), `ConfidentialMPTClawback` (89); encrypted amounts/commitments/proofs are opaque hex blobs supplied by an external prover
* `definitions.json` sync with rippled `develop` @ `fd2cc6dc`: +7 transaction types, +Sponsorship ledger entry, +23 fields (Sponsor set, ConfidentialTransfer set, `TakerPaysMPT`/`TakerGetsMPT`, `ReferenceHolding`, `SponsorFlags`), +8 result codes (`temBAD_MPT`, `temBAD_CIPHERTEXT`, `tefNO_DST_PARTIAL`, `tefBAD_PATH_COUNT`, `terLOCKED`, `terNO_PERMISSION`, `tecBAD_PROOF`, `tecNO_SPONSOR_PERMISSION`); TYPES renamed `UInt384`/`UInt512` → `Hash384`/`Hash512` (ordinals unchanged)
* TxFormat: common optional fields `Delegate`, `Sponsor`, `SponsorFlags`, `SponsorSignature`; formats for all 7 new transaction types
* Integration: `TestISponsorship` gated by `AmendmentGuard` (Sponsor/ConfidentialTransfer amendment ids added); nightly stand pinned to `xrpld 3.3.0-b1` @ `8306ac77` with Sponsor/ConfidentialTransfer enabled at genesis; all sponsorship integration tests pass against it (ledger-object round-trip, sponsored payment with SponsorSignature accepted as tesSUCCESS, tfDeleteObject)
* Unit tests: sponsor co-signing across all three flows with cryptographic verification over the shared preimage; `SponsorSignature` excluded from the preimage (kNotSigning) but round-trips through the binary codec
* Completeness pass over touched ledger objects: `LOAccountRoot` gains the XLS-68 counters (`SponsoredOwnerCount`, `SponsoringOwnerCount`, `SponsoringAccountCount`) plus previously missing `VaultID`/`LoanBrokerID` back-references; `LOMPToken` gains the six ConfidentialTransfer balance/key fields; `LOMPTokenIssuance` gains `DomainID`, `MutableFlags`, `ReferenceHolding`, `IssuerEncryptionKey`, `AuditorEncryptionKey`, `ConfidentialOutstandingAmount` (+11 ledger-object fields added to `definitions.json`)
* Fix binary-codec JSON decode of base-ten UInt64 fields (`MPTAmount`, `LockedAmount`, `OutstandingAmount`, `MaximumAmount`, `ConfidentialOutstandingAmount`): `Decode` now emits decimal strings matching rippled (`kSmdBaseTen`) instead of 16-digit hex — pre-existing gap surfaced by the new round-trip tests
* Tests: binary round-trips for all five ConfidentialMPT transactions and SponsorshipSet; validation tests mirroring rippled preflight; `TestIConfidentialMPT` negative e2e (bogus proof is rejected by ConfidentialTransfer domain logic, not the parser — proving the node parses our encoding)

## 10.5.1.0 07/04/2026
* Fix `SignAsBatchPart` with `TicketSequence`: when the outer Batch used a ticket and had no `Sequence`, the value `0` was applied only to the signing preimage while the serialized blob omitted the required `Sequence: 0` field, producing a malformed transaction on submit. The field is now written into the transaction as well; signatures are unaffected (the preimage already used `0`). Found by review on the 10.5.0.0 release PR
* Add a unit test covering the `TicketSequence`-present / `Sequence`-absent signing path (blob carries `Sequence: 0`, signature verifies over the zero-sequence preimage)
* Correct the `EncodeForSigningBatch` XML doc: `outerAccount` accepts a classic base58 r-address only (the 40-char hex form was never supported by this overload)
* Harden the nightly amendment stand: admin RPC/WS ports (5005/5006/6006) in `docker-compose.batchv11.yml` are now published to `127.0.0.1` only

## 10.5.0.0 07/03/2026
* **BREAKING**: Align Batch (XLS-56) signing with the `BatchV1_1` amendment ([rippled #6446](https://github.com/XRPLF/rippled/pull/6446), merged into `develop` 07/01/2026). The signing preimage now includes the outer `Account` (20 bytes) and outer `Sequence` (4 bytes) after the `BCH\0` prefix; `NetworkID` is removed from the preimage. `XrplBinaryCodec.EncodeForSigningBatch` signature changed to `(string outerAccount, uint outerSequence, uint flags, IEnumerable<string> txIDs)`. Signatures produced by the previous format are rejected by rippled once `BatchV1_1` is active
* `SignAsBatchPart` single-sig now binds the signature to the `BatchSigner` account id (`finishMultiSigningData` equivalent); inner multisign binds `owner(20) + signer(20)` account ids — both per the audit hardening in BatchV1_1
* Reject duplicate `BatchSigner` accounts locally (`SortBatchSigners`, `ValidateBatch`) and a `BatchSigner` equal to the outer `Account` — early fail instead of `temBAD_SIGNER` from the server
* **BREAKING**: Align `DelegateSet` (XLS-75) with the `PermissionDelegationV1_1` amendment — the delegate account field is `Authorize` (`sfAuthorize`), not `Delegate`: `IDelegateSet.Delegate`/`DelegateSet.Delegate`/`LODelegate.Delegate` renamed to `Authorize`; `TxFormat` requires `Authorize`
* Add `PermissionValueConverter` — rippled returns `Permission.PermissionValue` as a name string in JSON responses (a transaction type name or a granular permission like `TrustlineAuthorize`); the converter maps names to numeric values (transaction type code + 1; granular table 65537–65548 per `permissions.macro`) and accepts plain numbers
* Re-enable `TestIBatch` (19 tests) and `TestIDelegateSet` (2 tests) — previously `[Ignore]`d. New `AmendmentGuard` marks amendment-dependent integration tests inconclusive (skipped) when the node lacks the amendment, so CI on release images stays green and the tests run for real on a develop node
* Add a nightly-develop standalone stand for unreleased amendments: `.ci-config/Dockerfile.nightly` (pinned `xrpld` nightly from repos.ripple.com), `.ci-config/docker-compose.batchv11.yml`, `.ci-config/rippled.batchv11.cfg` (genesis up-votes via the `[amendments]` section — on rippled `develop` the `[features]` section no longer activates amendments in standalone)
* Add unit tests for the BatchV1_1 preimage layout and both signing modes with cryptographic verification, including negative checks that pre-V1_1-format signatures no longer verify
* Verified end-to-end against `xrpld 3.3.0-b0` (`develop`, commit `c92285f1`) with `BatchV1_1` and `PermissionDelegationV1_1` active: 21/21 integration tests pass; on the 3.2.0 CI image the full `TestI` suite runs 213 passed / 21 skipped / 0 failed

## Xrpl.X402 1.0.0 / Xrpl.X402.AspNetCore 1.0.0 06/23/2026
* **New package `Xrpl.X402`** — x402 (HTTP-402) agentic payments client for the XRP Ledger (t54 "XRPL exact scheme"). A `DelegatingHandler` that detects a 402 challenge, builds and locally signs an XRPL `Payment` (XRP or RLUSD/IOU), and retries with a `PAYMENT-SIGNATURE` header. Signs but does not submit — the facilitator settles
* Security: spending caps enforced before signing (XRP `MaxAmountDrops`; IOU fails closed without an explicit per-issuer cap), optional payTo/issuer allowlist, anti-double-pay, `LastLedgerSequence` capped by `maxTimeoutSeconds`
* Intent binding matches the t54 reference payer: `Payment.InvoiceID = SHA-256(invoiceId)`, a `MemoData` = hex(invoiceId), `payload.invoiceId`, and `SourceTag` from `extra.sourceTag` (configurable via `X402IntentBinding`); IOU payments include `SendMax`
* Verifiable Intent passthrough via `IVerifiableIntentProvider` (the SD-JWT chain itself is supplied by the caller)
* **New package `Xrpl.X402.AspNetCore`** — ASP.NET Core server middleware: a `RequirePayment` endpoint filter plus `LedgerSettlingFacilitator` (settles locally) and `T54Facilitator` (delegates to a t54 facilitator)
* Live interop with the t54 testnet facilitator confirmed on-chain for both XRP and RLUSD/IOU (`/verify` → `isValid:true`, `/settle` settles)

## 10.4.2.0 06/05/2026
* Fix thread-unsafe request id assignment in `RequestManager` — concurrent requests on a single connection (e.g. `Task.WhenAll` over several `BookOffers`) could collide on the same id and throw `Response with id '$<guid>' is already pending` or drop a pending promise. Removed the shared `nextId` field; each call now generates its own `Guid` and registers via a single atomic `ConcurrentDictionary.TryAdd`, enabling parallel requests on one connection
* Surface exceptions thrown by stream handlers (`OnLedgerClosed`, `OnTransaction`, etc.) through the `OnError` event instead of swallowing them into a debug trace — consumer bugs are now observable, while the message loop stays alive and a throwing `OnError` handler is contained
* Clarify in XML docs that `Xrpl.Client.Exceptions.TimeoutException` is not `System.TimeoutException` (it derives from `XrplException`), to avoid mismatched `catch` clauses

## 10.4.1.0 05/28/2026
* Fix `IouValue` (IOU token amount) parsing to accept a trailing decimal point (e.g. `"128700."`), aligning with `xrpl.js` / `ripple-binary-codec` and `rippled` `STAmount` reference behavior — previously the stricter validation regex rejected a value with no digits after the dot, breaking signing of transactions (e.g. `AMMDeposit` via WalletConnect) that carried such amounts
* Relax IOU value regex fractional group from `(\.(\d+))?` to `(\.(\d*))?` while adding a `(?=\.?\d)` lookahead that still requires at least one mantissa digit — so trailing/leading dots (`"128700."`, `".5"`) parse but bare-dot inputs (`"."`, `".e10"`) are rejected, matching BigNumber; deduplicate the regex by reusing the single `IouValue.ValueRegex` constant in `AmountValue.cs` and `ExtenstionHelpers.cs`
* Native XRP (drops) and MPT amount parsing unchanged; mantissa/exponent math, `ToString()` output, and `ToBytes()` round-trip preserved bit-for-bit for already-valid values
* Add unit tests verifying `"128700."` and `"1."` parse identically to their dot-less forms (same mantissa/exponent/precision and `ToBytes()` blob) and regression tests for existing values

## 10.4.0.0 05/13/2026
* Sync `Xrpl.BinaryCodec` enums with upstream `definitions.json` from [xrpl.js](https://github.com/XRPLF/xrpl.js)
* Add 24 missing `TransactionType` entries: XChain (8), Vault (6), Loan (9), LedgerStateFix, DelegateSet, Batch, NFTokenModify, PermissionedDomainSet/Delete, CredentialCreate/Accept/Delete, MPToken (4), DID (2), Oracle (2), AMMClawback
* Add 16 missing `LedgerEntryType` entries: Bridge, XChainOwnedClaimID, XChainOwnedCreateAccountClaimID, MPTokenIssuance, MPToken, Oracle, Credential, PermissionedDomain, Delegate, Vault, LoanBroker, Loan, DID, NegativeUNL, NFTokenOffer, NFTokenPage
* Add 7 missing `FieldType` entries: Number, Int32, Int64, UInt96, UInt384, UInt512, XChainBridge
* Add ~40 missing `Field` entries across all types; fix incorrect ordinals for DiscountedFee, VoteWeight, HookGrants
* Regenerate `EngineResult` with all 189 transaction result codes from protocol spec
* Add `terNO_DELEGATE_PERMISSION` (-85) to `definitions.json`
* Mark deprecated entries with `[Obsolete]`: HookSet, GeneratorMap, Contract, EnabledAmendments
* Refactor `EngineResult`, `TransactionType`, `LedgerEntryType` to partial-class architecture — hand-written infrastructure + auto-generated fields from `definitions.json`
* Add `Tools/GenerateEnums` — .NET console tool for regenerating enum files from `definitions.json` (`dotnet run --project Tools/GenerateEnums`)
* **XChain Bridge (XLS-38d):** Add 8 transaction models, 3 ledger objects (`LOBridge`, `LOXChainOwnedClaimID`, `LOXChainOwnedCreateAccountClaimID`), `XChainBridgeModel`, attestation models, and integration tests
* **Vault (XLS-65d):** Add 6 transaction models (`VaultCreate`, `VaultSet`, `VaultDelete`, `VaultDeposit`, `VaultWithdraw`, `VaultClawback`), `LOVault` ledger object, and integration tests
* **Lending Protocol (XLS-66d):** Add 9 transaction models (`LoanBrokerSet`, `LoanBrokerDelete`, `LoanBrokerCoverDeposit`, `LoanBrokerCoverWithdraw`, `LoanBrokerCoverClawback`, `LoanSet`, `LoanDelete`, `LoanManage`, `LoanPay`), `LOLoan` and `LOLoanBroker` ledger objects, and integration tests
* **DelegateSet (XLS-74d):** Add `DelegateSet` transaction model, `LODelegate` ledger object, and integration tests
* **LedgerStateFix:** Add `LedgerStateFix` transaction model and integration tests
* Fix `NumberType` serialization — rewrite from 8-byte raw ulong to 12-byte format (8-byte int64 mantissa + 4-byte int32 exponent) matching rippled Number class. Normalizes mantissa to [10^18, long.MaxValue]
* Add `CounterpartySignature` co-signing support for `LoanSet` — both broker and borrower sign the same preimage
* Add TxFormat entries and validation for all 25 new transaction types
* Add converter mappings for all new transaction and ledger entry types
* Add `LendingProtocol-Guide.md` and `LendingProtocol-Guide.ru.md` documentation

## 10.3.0.0 05/05/2026
* **BREAKING**: Migrate entire solution from `Newtonsoft.Json` to `System.Text.Json` — all models, converters, client infrastructure, wallet signing, binary codec
* **BREAKING**: Remove `dynamic` keyword from all production code — replace with `object`, `JsonNode`, `JsonElement` for iOS Full AOT compatibility
* **BREAKING**: Remove `Newtonsoft.Json` NuGet dependency from all projects (`Xrpl`, `Xrpl.BinaryCodec`, `Xrpl.AddressCodec`, `Xrpl.Keypairs`)
* Add centralized `XrplJsonOptions.Default` with all custom converters registered globally
* Add new converters: `DictionaryObjectConverter`, `EnumMemberValueConverter<T>`, `NumberOrStringConverter`, `ScientificDecimalConverter`, `TransactionTypeConverter`, `LedgerEntryTypeConverter`
* Migrate all `[JsonProperty]` → `[JsonPropertyName]`, `[JsonIgnore]` → `System.Text.Json.Serialization.JsonIgnore`
* Migrate all `JObject`/`JToken`/`JArray` → `JsonNode`/`JsonObject`/`JsonArray` in wallet signing, batch transactions, signer utilities
* Migrate all `JsonConvert.SerializeObject`/`DeserializeObject` → `JsonSerializer.Serialize`/`Deserialize`
* Add `ITransactionRequest.ToDictionary()` helper for safe `System.Text.Json` round-trip in tests
* Fix `SerializedType.ToJson()` return type — `object` → `JsonNode` to match `ISerializedType` contract
* Fix `ServerFeatures.FeatureInfo.Count` — `[JsonPropertyName("count")]` was inside XML doc comment, not applied to property
* Fix `ChannelAuthorize.RippleAmount` setter — `Convert.ToUInt32` → `Convert.ToUInt64` to prevent overflow at > 4294 XRP
* Fix `AccountingStateInfo.Duration` — `duration_us` field was parsed as milliseconds instead of microseconds (1000x inflation)
* Fix `LedgerTransaction.CloseTimeIso` and `LOLedger.CloseTimeIso` — add `FromStringDateTimeConverter` for consistent ISO 8601 parsing
* Fix `CredentialQuery.CredentialType` wire field — `credentialType` → `credential_type`
* Fix `Amount.FromJson` XRP branch — add null/type validation on `value` property to prevent `NullReferenceException`
* Fix `AccountId.FromJson` — explicit null check to prevent `DecodeAccountID(null)` crash
* Fix `Uint64` parsing — validate hex length after `0x` prefix to reject oversized inputs
* Fix `AssetPriceConverter.Write` — reject negative `int`/`long` values instead of silent `ulong` underflow
* Fix `OracleCurrencyConverter.Write` — reject currency codes > 20 ASCII bytes instead of silent truncation
* Fix `OracleHexStringConverter.Write` — remove content-sniffing that misidentified plain text as pre-encoded hex
* Fix `LOOracle` — add missing `OracleHexStringConverter` on `Provider`, `AssetClass`, `URI` properties (matching `OracleSet`)
* Fix `XrplBinaryCodec.EncodeForSigningClaim` — add null checks on `channel` and `amount` properties
* Fix `SimulateRequest.Transaction` — add explicit `TransactionRequestConverter` attribute for reliable polymorphic serialization
* Fix `LedgerObjectConverter` — extract shared `GetTypeForLedgerEntry()` helper, eliminating duplicated 23-type switch
* Fix `ScientificDecimalConverter` — parse raw token text via `decimal.Parse` instead of lossy `double` cast
* Fix `EnumMemberValueConverter` — remove permissive `Enum.TryParse` fallback that accepted numeric strings

## 10.2.0.0 03/05/2026
* Add `path_find` WebSocket command — `PathFind(create)`, `PathFindClose`, `PathFindStatus` methods with `PathFindCreateRequest`, `PathFindCloseRequest`, `PathFindStatusRequest` models and `PathFindResponse`
* Add `ripple_path_find` command — `RipplePathFind` method with `RipplePathFindRequest`, `RipplePathFindResponse`, `SourceCurrency` models
* Add `PathAlternative` shared model with `PathsComputed`, `PathsCanonical`, `SourceAmount`, `DestinationAmount`
* Add `Type` and `TypeHex` bitmask fields to `Path` model for path step type identification
* Fix `PathFindStream` — change `DestinationAmount`/`SendMax` from `decimal` to `Currency`, change `Id` from `Guid?` to `object`, replace `AlternativePath` with shared `PathAlternative`
* Fix message routing for `path_find` async follow-ups — `RequestManager.HandleResponse` now returns `(Response, Handled)` tuple, unhandled messages with `id` are routed to stream processing
* Add `TestEmitsPathFind` unit test with two sequential stream messages validation
* Add integration tests for `path_find` (create/close/status/stream) and `ripple_path_find` (basic/with source currencies)
* Add `ParseMPTID` utility for MPTokenIssuanceID (XLS-33) encoding/decoding — `GenerateMPTokenIssuanceID(sequence, issuer)` and `string.ParseMPTokenIssuanceID()` extension
* Add `MPTokenIssuanceIdData` model mirroring `NFTokenIdData` pattern (Sequence, Issuer, computed MPTokenIssuanceID)
* Add computed `MPTokenIssuanceID` property to `LOMPTokenIssuance` derived from `Sequence` + `Issuer`
* XLS-70 Credentials: full parity with `xrpl.js`
  * Add `deposit_authorized` request/response models (`DepositAuthorizedRequest`, `DepositAuthorized`) with optional XLS-70 `credentials` parameter
  * Implement `IXrplClient.DepositAuthorized(request, ct)` method
  * Add `CredentialIDs` (Vector256, optional) field to `Payment`, `EscrowFinish`, `AccountDelete`, `PaymentChannelClaim` models, validation and `TxFormat`
  * Extend `DepositPreauth` transaction with `AuthorizeCredentials` / `UnauthorizeCredentials` arrays and rewrite validation to enforce mutual exclusivity of `Authorize`/`Unauthorize`/`AuthorizeCredentials`/`UnauthorizeCredentials`
  * Fix broken `TxFormat[DepositPreauth]` (replaced PaymentChannelClaim fields with correct DepositPreauth fields including credential arrays)
  * Add shared `CredentialsValidator.ValidateCredentialsList` helper supporting both hex object IDs and wrapped `{ Credential: { Issuer, CredentialType } }` objects (max 8, hex format, no duplicates)
  * Fix binary codec: place `CredentialIDs` at `Vector256 nth=5` and move `HookNamespaces` to `nth=32` per rippled spec
  * Add `LedgerSpace.Credential = 'D'` and `Hashes.HashCredential(subject, issuer, credentialType)` helper to compute Credential ledger entry object IDs (SHA512Half)
  * Add unit tests for `CredentialsValidator`, extended `DepositPreauth` validation, and `CredentialIDs` validation across all four affected transactions
  * Add integration tests for `deposit_authorized` (with/without credentials) and end-to-end XLS-70 scenario: `CredentialCreate` → `CredentialAccept` → `AccountSet(asfDepositAuth)` → `DepositPreauth(AuthorizeCredentials)` → `Payment(CredentialIDs)`

## 10.1.6.0 15/04/2026
* Fix for Currency to HEX for currency with 1 or 2 symbol in name

## 10.1.5.0 14/04/2026
* Fix binary codec field codes for AMM Amount fields — `LPTokenOut` (20→25), `LPTokenIn` (21→26), `EPrice` (22→27), `Price` (23→28), `LPTokenBalance` (24→31)
* Add missing binary codec Amount field definitions: `BaseFeeDrops` (22), `ReserveBaseDrops` (23), `ReserveIncrementDrops` (24), `SignatureReward` (29), `MinAccountCreateAmount` (30)
* Add AMM lifecycle integration tests (16 tests): AMMCreate, AMMDeposit (SingleAsset, TwoAssets, LPToken), AMMWithdraw (LPToken, WithdrawAll, FullLP precision regression, SingleAsset, Simulate+Submit, TypedModel), AMMDelete (EmptyPool, NonEmptyPool, AfterPartialWithdraw), AMMVote

## 10.1.4.0 14/04/2026
* Fix `Currency.ValueAsNumber` setter precision — change format from `"G15"` to `"G16"` to preserve all 16 significant digits of XRPL token mantissa, preventing `tecAMM_INVALID_TOKENS` on full LP token withdrawal due to rounding up
* Add unit tests for `Currency` class — round-trip precision, `ValueAsXrp`, implicit operators, `CurrencyExtensions`, equality operators (39 tests)

## 10.1.3.0 11/04/2026
* Add `deep_freeze` and `deep_freeze_peer` fields to `TrustLine` model (XLS-77 Deep Freeze support)
* Add `Limit` field to `AccountLines` response
* Change `AccountLinesRequest.IgnoreDefault` type from `bool` to `bool?`
* Add `PseudoAccount` field to `AccountInfo` response
* Add `AMMID` field to `LOAccountRoot`

## 10.1.2.0 05/04/2026
* Fix `WaitForFinalTransactionOutcome` — `txnNotFound` was never recognized due to reading empty `Exception.Data` instead of `RippledException.Response.Error`, causing false `ValidationException` on successful submissions
* Replace generic `catch (Exception)` in `WaitForFinalTransactionOutcome` with split catch blocks: `RippledException` with `when` filter for `txnNotFound`, re-throw for other rippled errors, `XrplException` wrapper for unexpected errors
* Add null-safety for `Response` in `XrplErrorClassifier.Classify(RippledException)`

## 10.1.1.0 05/04/2026
* Add new ripple state flags support

## 10.1.0.1 03/04/2026
* Convert XrplErrorClassifier methods to extension methods for fluent error classification (`exception.Classify()`)
* Add try-catch around response deserialization in RequestManager.Resolve — reject promise and rethrow on failure
* Integrate XrplErrorClassifier into Connection.IOnMessageFastPath error handler with user-friendly error messages
* Change Submit/SubmitAndWait `autofill` default from `false` to `true`
* Add `AllowTrustLineLocking` flag to AccountInfoAccountFlags
* Fix NoRippleCheck `Transactions` deserialization — use `List<ITransactionRequest>` with polymorphic `TransactionRequestConverter`
* Fix CurrencyConverter to handle `JsonToken.Integer` for XRP amounts

## 10.1.0.0 02/04/2026
* Add optional CancellationToken support for all client requests (IXrplClient, Connection, RequestManager)
* Thread CancellationToken through all Sugar methods (Autofill, Submit, Balances, GetOrderBook, GetFeeXrp, GetLedgerIndex)
* Make RequestManager.Resolve idempotent — no longer throws when promise is already cancelled/timed out
* Add safe async dispose of CancellationTokenRegistration to prevent deadlocks in cancellation callbacks
* Add 9 unit and E2E tests for CancellationToken (cancellation, race conditions, timeout priority, connection isolation)
* Full backward compatibility — all CancellationToken parameters are optional with default value

## 10.0.2.1 30/03/2026
* Fix polymorphic ledger entry deserialization for `account_objects`
* Fix `ledger_data` JSON response mapping for `state`
* Add missing `ledger`, `validated`, and ledger entry type filter support

## 10.0.2 25/03/2026
* Add XRPL error classifier with normalized `XrplErrorInfo`
* Add structured XRPL error metadata: category, subject, retryable/user-fixable flags, command, field, and warnings
* Add tests and documentation for XRPL error classification
* Minor RequestManager cleanup for pending response handling

## 10.0.1.1 24/03/2026
* Fix ErrorResponse
* Fix RippledException when error in response

## 10.0.1 20/03/2026
* Refactor gateway_balances request
* Add v1 transaction response support
* Fix test account builder
* Refactor metadata with converters for ledger types
* Add missing ledger entry request parameters
* Add wallet FromPrivateKey method
* Fix LedgerObject date conversion
* Add mnemonic verification

## 10.0.0.1-mptmeta 02/13/2026
* MPToken Metadata parser

## 10.0.0
* Upgrade to .NET 10.0
* TokenEscrow (XLS-85) — extended escrow support for fungible tokens (IOU/MPT)
* Credentials (XLS-70) — CredentialCreate, CredentialAccept, CredentialDelete transactions, LOCredential ledger entry
* PermissionedDomain (XLS-80) — PermissionedDomainSet, PermissionedDomainDelete transactions, LOPermissionedDomain ledger entry
* Permissioned DEX (XLS-81) — DomainID and tfHybrid flag for OfferCreate, DomainID for Payment

## 9.8.3-implicit 02/11/2026
* Add Currency uint implicit conversion

## 9.8.2-apiVersion 02/09/2026
* Fix API version set

## 9.8.1-connection 02/06/2026
* Connection stabilization improvements
* Minor config fix
* Documentation updates

## 9.8.0 02/04/2026
* Mnemonic wallet generator
* Xumm numbers generator
* Connection stabilization and errored tasks resolution
* Update account flags and clear flags fix
* Add test data init
* Fix connection issues

## 9.7.2 01/24/2026
* Fix race condition null exception in DID handling

## 9.7.1 01/24/2026
* Add JSON writer for converters (DID fix)

## 9.7.0 01/22/2026
* Add DID (Decentralized Identifier) support — DIDSet, DIDDelete transactions
* Add Clawback transaction support
* Add AMMClawback transaction support
* Add Oracle Set/Delete transactions (XLS-47 Price Feeds)

## 9.6.2 01/17/2026
* Add signer locator (WalletLocator) encoding
* Update connection logic
* Fix encoding issues
* Documentation updates

## 9.6.1 12/16/2025
* Add connection status tracking
* Fix namespace for BalanceChanges

## 9.6.0 12/15/2025
* Add MPToken support (MPTokenAuthorize, MPTokenIssuanceCreate, MPTokenIssuanceDestroy, MPTokenIssuanceSet)
* Add currency extensions
* Add features request

## 9.5.0 12/13/2025
* Signing refactoring — batch signing, in-batch multisign
* Refactor autofill logic
* Refactor TX common models
* Fix encoding and sign model issues
* Add sign batch tests

## 9.4.1 12/01/2025
* Add Pbkdf2 for wallet from text

## 9.4.0 11/18/2025
* Upgrade to .NET 9
* Add RequestFailurePolicy and status wait for connection
* Add reconnection stop flag and timeout for connection
* Fix on user disconnect and ping policy
* LastLedgerSequence can be null
* Refactoring and test fixes

## 9.3.0 11/12/2025
* Connection manager fix — auto-reconnect, connection ping-pong, reconnection progress

## 9.2.1 11/10/2025
* Fix Payment deliverMax serialization

## 9.2.0 11/10/2025
* Add deliverMax support
* Add warning notifications
* NFT parse update

## 9.1.5 11/09/2025
* Add destination interface

## 9.1.4 11/09/2025
* Fix ledger response

## 9.1.3 11/02/2025
* Fix WebAssembly (WASM) support error
* Add Blazor test app

## 9.1.2 10/16/2025
* Fix autofill fee calculation

## 9.1.1 10/14/2025
* Add ledger entry types
* Fix serialization error

## 9.1.0 10/14/2025
* Add Batch transaction support with multi-signature
* Add wallet from any text
* Add simulate request
* Add batch enum to base enums
* Fix flag references for in-batch TX serialization
* Update AccountInfo and AccountObjects
* Minor fixes and optimization

## 9.0.8 06/29/2025
* Add XLS-46d (dynamic NFTs) transaction support
* Fix AMM Withdraw flags
* Fix client issues

## 9.0.7 06/01/2025
* Fix NFTokenIds

## 9.0.6-beta 05/26/2025
* Fix Submit and wait logic
* Add TxV2 request/response

## 9.0.3-beta 05/24/2025
* Refactoring for API v2 — stream custom converter
* Add BalanceChanges
* Add Book equals and AMM deposit flag
* Fix response ID format for re-using
* Fix ledger entry response
* Update client and packages
* Fix v2 adaptation and unsubscribe
* netstandard optimization and currency extensions
* Fix AMM TX encoding
* Add mnemonic support

## 1.0.6 06/19/2022
* Fix Trustlines JsonProperty and Limit default (thanks @ReneBrauwers)

## 1.0.5 06/09/2022
* Add payment channel encoding

## 1.0.3 05/26/2022
* Update XLS-20 fields

## 1.0.2 03/31/2022
* Fix tests and initial setup

## 1.0.0 04/30/2023
* Initial Release of XrplCSharp
