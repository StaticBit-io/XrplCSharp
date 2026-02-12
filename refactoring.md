# Refactoring Notes

## Hex String Extension Methods Duplication

There are two identical extension method implementations for hex string conversion that should be unified in the future:

1. **`Xrpl/Utils/StringConversion.cs`** — `ConvertStringToHex()` and `FromHexString()` extension methods
2. **`Xrpl/Client/Extensions/ExtensionHelpers.cs`** — exact same methods duplicated

Both use BouncyCastle `Hex.ToHexString` (produces **lowercase** hex) and manual byte parsing for decoding.

Additionally, **`Xrpl/Models/Utils/HexStringHelper.cs`** was introduced for Credential/SignerList models with:
- `NormalizeToHex()` — auto-detects hex vs plain text, normalizes to **UPPERCASE**, validates length
- `FromHex()` — decodes hex to UTF-8, trims trailing `\0` bytes
- `IsValidHex()` — validates hex format

### Key Differences

| Feature | FromHexString (extension) | HexStringHelper |
|---|---|---|
| Call style | `"hex".FromHexString()` | `HexStringHelper.FromHex("hex")` |
| Hex case output | lowercase (BouncyCastle) | UPPERCASE (Convert.ToHexString) |
| Null byte trimming | No | Yes |
| Auto-detect hex/text | No | Yes (NormalizeToHex) |
| Length validation | No | Yes (maxBytes param) |
| Used in | Memo, MPToken, AccountNFTs, Currency | Credential models, LOSignerList, LOCredential |

### TODO
- Consider unifying into a single utility class
- Decide on lowercase vs uppercase hex convention project-wide
- Replace duplicated ExtensionHelpers.cs with a reference to StringConversion.cs or vice versa
- Evaluate whether extension method style or static method style is preferred

---

## Migration: Newtonsoft.Json → System.Text.Json

### Overview

The entire project heavily depends on `Newtonsoft.Json`. Migration to `System.Text.Json` (STJ) will improve performance, reduce NuGet dependency footprint, and align with the modern .NET ecosystem. This is a **large-scale refactoring** that should be done incrementally, module by module.

### Impact Assessment

#### NuGet Package References (4 projects)
| Project | File |
|---|---|
| `Xrpl` | `Xrpl/Xrpl.csproj` |
| `Xrpl.BinaryCodec` | `Base/Xrpl.BinaryCodec/Xrpl.BinaryCodec.csproj` |
| `Xrpl.AddressCodec` | `Base/Xrpl.AddressCodec/Xrpl.AddressCodec.csproj` |
| `Xrpl.Keypairs` | `Base/Xrpl.Keypairs/Xrpl.Keypairs.csproj` |

#### Source File Counts (using Newtonsoft.Json)
| Layer | Files with `using Newtonsoft.Json` | JObject/JArray/JToken usage |
|---|---|---|
| `Xrpl/` (main client) | ~130 files | ~210 references |
| `Base/` (BinaryCodec, etc.) | ~29 files | ~72 references |
| `Tests/` | ~20+ files | ~87 references |

#### Attribute Usage
| Attribute | Count (approx) | STJ Equivalent |
|---|---|---|
| `[JsonProperty("name")]` | ~500+ | `[JsonPropertyName("name")]` |
| `[JsonProperty(NullValueHandling = ...)]` | ~30+ | `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` or global `JsonSerializerOptions.DefaultIgnoreCondition` |
| `[JsonConverter(typeof(...))]` | ~150+ | `[JsonConverter(typeof(...))]` (same attribute name, different base class) |
| `[JsonIgnore]` | ~40+ | `[JsonIgnore]` (same name, different namespace) |

### Custom Converters (17 files in `Xrpl/Client/Json/Converters/`)

Each converter must be rewritten from `Newtonsoft.Json.JsonConverter` to `System.Text.Json.Serialization.JsonConverter<T>`.

| Converter | Complexity | Risk | Notes |
|---|---|---|---|
| **LOConverter** (LedgerObjectConverter.cs) | HIGH | HIGH | Polymorphic deserialization of ~30+ ledger entry types via `LedgerEntryType` discriminator. In STJ use `JsonDerivedType` or manual `Utf8JsonReader` switching. **Most complex converter.** |
| **TransactionResponseConverter** | HIGH | HIGH | Polymorphic deserialization of transaction responses. Similar pattern to LOConverter. |
| **TransactionRequestConverter** | HIGH | HIGH | Polymorphic serialization/deserialization of transaction requests. |
| **CurrencyConverter** | MEDIUM | HIGH | Handles XRP drops vs IOU currency vs MPT amount — complex conditional logic. Also includes `IssuedCurrencyConverter`. |
| **ServerFeaturesConverter** | MEDIUM | MEDIUM | Deserializes `Dictionary<string, FeatureInfo>` with custom logic. |
| **LedgerBinaryConverter** | MEDIUM | MEDIUM | Binary ledger data handling. |
| **MetaBinaryConverter** | MEDIUM | MEDIUM | Transaction metadata in binary format. |
| **TransactionOrHashConverter** | MEDIUM | MEDIUM | Union type: string hash OR transaction object. |
| **LONFTokenConverter** | MEDIUM | MEDIUM | NFToken page deserialization. |
| **OracleConverters** (3 converters) | MEDIUM | MEDIUM | `AssetPriceConverter`, `OracleCurrencyConverter`, `OracleHexStringConverter`. |
| **StringOrArrayConverter** | LOW | LOW | Handles `string | string[]` union. |
| **LedgerIndexConverter** | LOW | LOW | `string | number` ledger index. |
| **GenericStringConverter\<T\>** | LOW | LOW | Generic enum-to-string. |
| **UInt64StringJsonConverter** | LOW | LOW | `ulong ↔ string`. |
| **UInt64HexJsonConverter** | LOW | LOW | `ulong ↔ hex string`. |
| **RippleDateTimeConverter** | LOW | MEDIUM | Ripple epoch (946684800 offset) ↔ DateTime. Must preserve exact second precision. |
| **FromStringDateTimeConverter** | LOW | LOW | String date parsing. |
| **StreamTypeListConverter** (Enums/) | LOW | LOW | Enum list serialization. |

### JObject / JToken Usage — Critical Areas

`JObject`, `JArray`, `JToken` are used **extensively** as dynamic data containers. STJ equivalent is `JsonElement` (read-only) or `JsonNode` (mutable). Key difference: `JObject` is mutable, `JsonElement` is NOT.

#### Highest-Risk Areas (require careful rewrite)

| File(s) | JObject/JToken refs | Usage Pattern | STJ Approach |
|---|---|---|---|
| `Xrpl/Wallet/XrplWallet.cs` | 55 | Transaction signing, serialization to/from JSON for BinaryCodec | `JsonNode` or strongly-typed DTOs |
| `Xrpl/Wallet/BatchSigningHelper.cs` | 49 | Batch transaction manipulation | `JsonNode` for mutable access |
| `Xrpl/Wallet/SignerUtilities.cs` | 27 | Multi-signing, field manipulation | `JsonNode` |
| `Xrpl/Models/Utils/BatchNormalizer.cs` | 13 | Batch normalization logic | `JsonNode` |
| `Xrpl/Models/Utils/BatchUtils.cs` | 11 | Batch utilities | `JsonNode` |
| `Xrpl/Sugar/Submit.cs` | 7 | Submit transaction flow | `JsonNode` or `JsonElement` |
| `Xrpl/Sugar/Autofill.cs` | 4 | Autofill transaction fields | `JsonNode` |
| `Xrpl/Sugar/LedgerSequenceHelper.cs` | 8 | Ledger sequence tracking | `JsonNode` |
| `Base/Xrpl.BinaryCodec/XrplBinaryCodec.cs` | 8 | Core binary codec entry point | `JsonNode` |
| `Base/Xrpl.BinaryCodec/Types/Amount.cs` | 9 | Amount serialization | `JsonNode` |
| `Base/Xrpl.BinaryCodec/Types/StObject.cs` | 8 | Serialized object handling | `JsonNode` |
| `Base/Xrpl.BinaryCodec/Types/PathSet.cs` | 9 | Path set for payments | `JsonNode` |
| `Tests/Xrpl.Tests/Wallet/TestUSignerUtilities.cs` | 50 | Signer test data | `JsonNode` |

#### `dynamic` Type Usage
~200+ references to `dynamic` in `Xrpl/` — many are `Dictionary<string, dynamic>` used in validation logic (`Validation.cs`, `Flags.cs`). These interact with Newtonsoft's ability to serialize/deserialize `dynamic` (which STJ does NOT support natively). Consider replacing with `Dictionary<string, object>` + explicit casting or `JsonNode`.

### JsonSerializerSettings → JsonSerializerOptions

| Location | Current Newtonsoft Settings | STJ Equivalent |
|---|---|---|
| `RequestManager.cs` (line 46-48) | `NullValueHandling.Ignore`, `DateTimeZoneHandling.Utc` | `DefaultIgnoreCondition = WhenWritingNull`, custom DateTime handling |
| `Common.cs` (line 293-294) | `NullValueHandling.Ignore` | `DefaultIgnoreCondition = WhenWritingNull` |
| `Common.cs` (line 717-718) | `NullValueHandling.Ignore` | Same |
| `IXrplClient.cs` (line 508-513) | `NullValueHandling.Ignore`, `JsonSerializer.CreateDefault()` | `JsonSerializerOptions` with converters |

### Key Behavioral Differences (Newtonsoft vs STJ)

| Behavior | Newtonsoft.Json | System.Text.Json | Impact |
|---|---|---|---|
| Case sensitivity | Case-insensitive by default | **Case-sensitive** by default | Set `PropertyNameCaseInsensitive = true` globally |
| Null handling | `NullValueHandling.Ignore` per-property or global | `DefaultIgnoreCondition = WhenWritingNull` global, `[JsonIgnore(Condition)]` per-property | Attribute syntax changes |
| `dynamic` support | Full support | **Not supported** | Replace with `JsonNode`, `JsonElement`, or typed objects |
| `JObject`/`JToken` | Mutable, full LINQ support | `JsonNode` (mutable) or `JsonElement` (read-only, faster) | Major rewrite in Wallet/BinaryCodec |
| Polymorphic deser | `JsonConverter` with `JObject.Load()` then `ToObject()` | `JsonDerivedType`, `JsonPolymorphic`, or manual `Utf8JsonReader` | Rewrite all polymorphic converters |
| Comments in JSON | Allowed by default | **Rejected** by default | Set `ReadCommentHandling = JsonCommentHandling.Skip` if needed |
| Trailing commas | Allowed by default | **Rejected** by default | Set `AllowTrailingCommas = true` if needed |
| `$type` discriminator | Built-in for polymorphism | Not available (use `JsonDerivedType` in .NET 7+) | Different approach needed |
| Circular references | `ReferenceLoopHandling` | `ReferenceHandler.Preserve` or `.IgnoreCycles` | Check if used |
| Property ordering | Preserves declaration order | Preserves declaration order | Same |
| Enum serialization | `StringEnumConverter` | `JsonStringEnumConverter` | Simple rename |
| DateTime handling | Flexible parsing, `DateTimeZoneHandling` | ISO 8601 only by default | Need custom converter for Ripple epoch |
| `JsonConvert.SerializeObject` | Static method | `JsonSerializer.Serialize()` | Simple rename (~50+ calls) |
| `JsonConvert.DeserializeObject<T>` | Static method | `JsonSerializer.Deserialize<T>()` | Simple rename (~30+ calls) |
| Private setters | Serialized by default | **Ignored** by default | Set `IncludeFields = true` or use `[JsonInclude]` |
| Read-only properties | Serialized | Serialized | Same |

### Migration Strategy (Recommended Order)

#### Phase 1: Base Libraries (lowest risk, fewest dependencies)
1. **`Xrpl.AddressCodec`** — minimal JSON usage, mostly just NuGet reference
2. **`Xrpl.Keypairs`** — minimal JSON usage
3. **`Xrpl.BinaryCodec`** — heavier use of `JObject`/`JToken` in type serialization (`StObject`, `Amount`, `PathSet`, `Currency`, etc.). Replace with `JsonNode`.

#### Phase 2: Models Layer
4. **`Xrpl/Models/Common/`** — `Currency.cs`, `PriceData.cs` — replace `[JsonProperty]` → `[JsonPropertyName]`, update `NullValueHandling`
5. **`Xrpl/Models/Transactions/`** — ~40+ files with `[JsonProperty]`, `[JsonConverter]` attributes. Bulk find-replace possible for simple cases.
6. **`Xrpl/Models/Ledger/`** — ledger object models, same pattern
7. **`Xrpl/Models/Methods/`** — request/response models
8. **`Xrpl/Models/Subscriptions/`** — stream models
9. **`Xrpl/Models/Enums/`** — `StreamTypeListConverter`, `LedgerEntryFilter`

#### Phase 3: Converters (highest risk)
10. **Simple converters first**: `UInt64StringJsonConverter`, `UInt64HexJsonConverter`, `GenericStringConverter`, `LedgerIndexConverter`, `StringOrArrayConverter`, `FromStringDateTimeConverter`
11. **Medium converters**: `RippleDateTimeConverter`, `CurrencyConverter`, `OracleConverters`, `ServerFeaturesConverter`, `MetaBinaryConverter`, `LedgerBinaryConverter`
12. **Complex polymorphic converters**: `LOConverter`, `TransactionResponseConverter`, `TransactionRequestConverter`, `TransactionOrHashConverter`, `LONFTokenConverter`

#### Phase 4: Client Infrastructure
13. **`RequestManager.cs`** — serialization settings, request/response handling
14. **`connection.cs`** — WebSocket message parsing
15. **`IXrplClient.cs`** — client interface and default serializer

#### Phase 5: Sugar & Wallet (highest JObject usage)
16. **`Xrpl/Sugar/`** — `Submit.cs`, `Autofill.cs`, `LedgerSequenceHelper.cs`
17. **`Xrpl/Wallet/`** — `XrplWallet.cs` (55 JObject refs!), `Signer.cs`, `SignerUtilities.cs`, `BatchSigningHelper.cs`

#### Phase 6: Tests
18. Update all test projects to use STJ
19. Remove Newtonsoft.Json NuGet references from all `.csproj` files

### Converter Migration Patterns

#### Simple property converter (Newtonsoft → STJ)

**Before (Newtonsoft):**
```csharp
public class UInt64StringJsonConverter : JsonConverter<ulong>
{
    public override ulong ReadJson(JsonReader reader, Type objectType, ulong existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var str = reader.Value?.ToString();
        return ulong.Parse(str);
    }

    public override void WriteJson(JsonWriter writer, ulong value, JsonSerializer serializer)
    {
        writer.WriteValue(value.ToString());
    }
}
```

**After (STJ):**
```csharp
public class UInt64StringJsonConverter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        return ulong.Parse(str);
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
```

#### Polymorphic converter (LOConverter pattern)

**Before (Newtonsoft):**
```csharp
public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
{
    JObject jo = JObject.Load(reader);
    var type = jo["LedgerEntryType"]?.ToString();
    BaseLedgerEntry entry = type switch
    {
        "AccountRoot" => jo.ToObject<LOAccountRoot>(serializer),
        "Offer" => jo.ToObject<LOOffer>(serializer),
        // ... 30+ types
    };
    return entry;
}
```

**After (STJ):**
```csharp
public override BaseLedgerEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
{
    using var doc = JsonDocument.ParseValue(ref reader);
    var root = doc.RootElement;
    var type = root.GetProperty("LedgerEntryType").GetString();

    var raw = root.GetRawText();
    return type switch
    {
        "AccountRoot" => JsonSerializer.Deserialize<LOAccountRoot>(raw, options),
        "Offer" => JsonSerializer.Deserialize<LOOffer>(raw, options),
        // ... 30+ types
    };
}
```
**Warning:** Double-parse overhead with `GetRawText()` + `Deserialize`. Alternative: use `JsonNode.Parse()` for a single pass but lose some perf. Or use .NET 9 `JsonDerivedType` with custom discriminator if applicable.

#### JObject → JsonNode migration

**Before (Newtonsoft):**
```csharp
JObject tx = JObject.FromObject(transaction);
tx["SigningPubKey"] = "";
tx.Remove("TxnSignature");
string json = tx.ToString();
```

**After (STJ):**
```csharp
JsonNode tx = JsonSerializer.SerializeToNode(transaction);
tx["SigningPubKey"] = "";
tx.AsObject().Remove("TxnSignature");
string json = tx.ToJsonString();
```

#### JsonProperty → JsonPropertyName

**Before:**
```csharp
[JsonProperty("currency", NullValueHandling = NullValueHandling.Ignore)]
public string CurrencyCode { get; set; }
```

**After:**
```csharp
[JsonPropertyName("currency")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public string CurrencyCode { get; set; }
```

### Areas Requiring Extra Attention

1. **Ripple DateTime epoch**: `RippleDateTimeConverter` uses offset 946684800 (Jan 1, 2000 UTC). Must replicate exactly — off-by-one-second errors will break transactions.

2. **Currency amount serialization**: XRP uses drops (string number), IOU uses `{currency, issuer, value}` object, MPT uses `{mpt_issuance_id, value}`. `CurrencyConverter` handles all three — **critical path** for all transactions.

3. **Polymorphic ledger entries**: `LOConverter` handles 30+ types. Missing a type → silent data loss. Must test every ledger entry type.

4. **`dynamic` in Validation.cs and Flags.cs**: ~200+ usages. Newtonsoft handles `dynamic` natively; STJ does not. Must replace with `JsonNode`, `Dictionary<string, object>`, or strongly-typed models.

5. **Wallet signing flow**: `XrplWallet.cs` (55 JObject refs) manipulates transaction JSON for signing. Any serialization difference → invalid signatures → **broken transactions**.

6. **BinaryCodec integration**: `XrplBinaryCodec.cs` converts between JSON and binary. Uses `JObject` throughout. Must maintain exact byte-level compatibility.

7. **WebSocket message parsing**: `connection.cs` and `RequestManager.cs` parse raw JSON from rippled. Any parsing difference → broken protocol communication.

8. **Case sensitivity**: Newtonsoft is case-insensitive by default. STJ is case-sensitive. The XRP Ledger protocol uses PascalCase for field names but some responses may use different casing. Set `PropertyNameCaseInsensitive = true` globally.

9. **Private setter deserialization**: Newtonsoft deserializes into private setters by default. STJ does not. Properties with private setters must be annotated with `[JsonInclude]` or the class must use constructor-based deserialization. Note: `IncludeFields` does NOT help with private setters — it only affects public fields.

10. **Null token handling**: Newtonsoft's `JToken.Type == JTokenType.Null` check has no direct equivalent in `JsonElement`. Use `element.ValueKind == JsonValueKind.Null`.

11. **TypeNameHandling / PreserveReferencesHandling**: STJ does NOT support `TypeNameHandling` (embedding `$type` discriminator). If Newtonsoft uses this anywhere in the project, it will be a migration blocker — must verify before starting. `PreserveReferencesHandling` has a limited equivalent via `ReferenceHandler.Preserve` in STJ, but the `$id`/`$ref` format differs. **Pre-migration checklist**: grep for `TypeNameHandling`, `PreserveReferencesHandling`, `$type`, `$id`, `$ref` in the codebase to confirm they are not used.

### Testing Strategy

#### Unit Tests (per-converter)
- For each of the 17 converters, write round-trip tests: serialize → deserialize → compare
- Test edge cases: null values, empty strings, missing fields, unknown enum values
- Compare output byte-for-byte with Newtonsoft version during migration

#### Integration Tests (per-module)
- Run ALL existing unit tests after each phase
- Run ALL integration tests against DevNet after Phase 3 (converters) and Phase 5 (wallet)
- **Critical**: Run signing tests — verify that signed transactions produce identical hashes

#### Binary Codec Compatibility
- Serialize known transactions with both Newtonsoft and STJ
- Compare binary output byte-for-byte
- Use test vectors from `xrpl.js` or rippled for validation

#### Regression Testing Checklist
- [ ] All existing unit tests pass (Tests/Xrpl.Tests, Tests/Xrpl.BinaryCodec.Test, Tests/Xrpl.Keypairs.Test, Tests/Xrpl.AddressCodec.Test)
- [ ] All integration tests pass against DevNet (CredentialCreate, PermissionedDomain, DID, Oracle, Clawback, AMM, Batch, etc.)
- [ ] Transaction signing produces identical hashes with both serializers
- [ ] BinaryCodec serialization/deserialization is byte-identical
- [ ] WebSocket communication with rippled nodes works correctly
- [ ] Blazor WebAssembly client still functions (STJ is the default in Blazor — this should improve)
- [ ] MAUI client still functions
- [ ] Console test client still functions

#### Performance Benchmarks (optional)
- Compare serialization/deserialization speed
- Compare memory allocation (STJ uses Utf8JsonReader/Writer which is more efficient)
- STJ should be significantly faster in hot paths (WebSocket message parsing, transaction signing)

### Blazor/MAUI Benefits
- STJ is the native serializer in Blazor WebAssembly — removing Newtonsoft.Json will **reduce bundle size**
- MAUI apps benefit from STJ's lower memory allocations
- No more linker issues with Newtonsoft.Json in AOT compilation scenarios

### Estimated Effort
| Phase | Files | Effort | Risk |
|---|---|---|---|
| Phase 1: Base Libraries | ~30 | 2-3 days | LOW |
| Phase 2: Models Layer | ~80 | 3-5 days | LOW-MEDIUM |
| Phase 3: Converters | 17 | 5-7 days | HIGH |
| Phase 4: Client Infrastructure | ~5 | 2-3 days | HIGH |
| Phase 5: Sugar & Wallet | ~10 | 5-7 days | CRITICAL |
| Phase 6: Tests | ~20 | 2-3 days | LOW |
| **Total** | **~160 files** | **~20-30 days** | |
