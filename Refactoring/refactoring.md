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

## ✅ Migration: Newtonsoft.Json → System.Text.Json — COMPLETED

Completed in commits `632e55b` (dynamic → object) and `8abd4be` (Newtonsoft → System.Text.Json).
242 files changed. All converters rewritten. All `dynamic` removed. 0 Newtonsoft references remain.
