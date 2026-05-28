# Changes

### 10.4.1.0 05/28/2026
* Fix `IouValue` (IOU token amount) parsing to accept a trailing decimal point (e.g. `"128700."`), aligning with `xrpl.js` / `ripple-binary-codec` and `rippled` `STAmount` reference behavior — previously the stricter validation regex rejected a value with no digits after the dot, breaking signing of transactions (e.g. `AMMDeposit` via WalletConnect) that carried such amounts
* Relax IOU value regex fractional group from `(\.(\d+))?` to `(\.(\d*))?` in `AmountValue.cs` and `ExtenstionHelpers.cs`; deduplicate the regex by reusing the single `IouValue.ValueRegex` constant
* Native XRP (drops) and MPT amount parsing unchanged; mantissa/exponent math, `ToString()` output, and `ToBytes()` round-trip preserved bit-for-bit for already-valid values
* Add unit tests verifying `"128700."` and `"1."` parse identically to their dot-less forms (same mantissa/exponent/precision and `ToBytes()` blob) and regression tests for existing values

### 10.4.0.0 05/13/2026
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

### 10.3.0.0 05/05/2026
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

### 10.2.0.0 03/05/2026
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

### 10.1.6.0 15/04/2026
* Fix for Currency to HEX for currency with 1 or 2 symbol in name

### 10.1.5.0 14/04/2026
* Fix binary codec field codes for AMM Amount fields — `LPTokenOut` (20→25), `LPTokenIn` (21→26), `EPrice` (22→27), `Price` (23→28), `LPTokenBalance` (24→31)
* Add missing binary codec Amount field definitions: `BaseFeeDrops` (22), `ReserveBaseDrops` (23), `ReserveIncrementDrops` (24), `SignatureReward` (29), `MinAccountCreateAmount` (30)
* Add AMM lifecycle integration tests (16 tests): AMMCreate, AMMDeposit (SingleAsset, TwoAssets, LPToken), AMMWithdraw (LPToken, WithdrawAll, FullLP precision regression, SingleAsset, Simulate+Submit, TypedModel), AMMDelete (EmptyPool, NonEmptyPool, AfterPartialWithdraw), AMMVote

### 10.1.4.0 14/04/2026
* Fix `Currency.ValueAsNumber` setter precision — change format from `"G15"` to `"G16"` to preserve all 16 significant digits of XRPL token mantissa, preventing `tecAMM_INVALID_TOKENS` on full LP token withdrawal due to rounding up
* Add unit tests for `Currency` class — round-trip precision, `ValueAsXrp`, implicit operators, `CurrencyExtensions`, equality operators (39 tests)

### 10.1.3.0 11/04/2026
* Add `deep_freeze` and `deep_freeze_peer` fields to `TrustLine` model (XLS-77 Deep Freeze support)
* Add `Limit` field to `AccountLines` response
* Change `AccountLinesRequest.IgnoreDefault` type from `bool` to `bool?`
* Add `PseudoAccount` field to `AccountInfo` response
* Add `AMMID` field to `LOAccountRoot`

### 10.1.2.0 05/04/2026
* Fix `WaitForFinalTransactionOutcome` — `txnNotFound` was never recognized due to reading empty `Exception.Data` instead of `RippledException.Response.Error`, causing false `ValidationException` on successful submissions
* Replace generic `catch (Exception)` in `WaitForFinalTransactionOutcome` with split catch blocks: `RippledException` with `when` filter for `txnNotFound`, re-throw for other rippled errors, `XrplException` wrapper for unexpected errors
* Add null-safety for `Response` in `XrplErrorClassifier.Classify(RippledException)`

### 10.1.1.0 05/04/2026
* Add new ripple state flags support

### 10.1.0.1 03/04/2026
* Convert XrplErrorClassifier methods to extension methods for fluent error classification (`exception.Classify()`)
* Add try-catch around response deserialization in RequestManager.Resolve — reject promise and rethrow on failure
* Integrate XrplErrorClassifier into Connection.IOnMessageFastPath error handler with user-friendly error messages
* Change Submit/SubmitAndWait `autofill` default from `false` to `true`
* Add `AllowTrustLineLocking` flag to AccountInfoAccountFlags
* Fix NoRippleCheck `Transactions` deserialization — use `List<ITransactionRequest>` with polymorphic `TransactionRequestConverter`
* Fix CurrencyConverter to handle `JsonToken.Integer` for XRP amounts

### 10.1.0.0 02/04/2026
* Add optional CancellationToken support for all client requests (IXrplClient, Connection, RequestManager)
* Thread CancellationToken through all Sugar methods (Autofill, Submit, Balances, GetOrderBook, GetFeeXrp, GetLedgerIndex)
* Make RequestManager.Resolve idempotent — no longer throws when promise is already cancelled/timed out
* Add safe async dispose of CancellationTokenRegistration to prevent deadlocks in cancellation callbacks
* Add 9 unit and E2E tests for CancellationToken (cancellation, race conditions, timeout priority, connection isolation)
* Full backward compatibility — all CancellationToken parameters are optional with default value

### 10.0.2.1 30/03/2026
* Fix polymorphic ledger entry deserialization for `account_objects`
* Fix `ledger_data` JSON response mapping for `state`
* Add missing `ledger`, `validated`, and ledger entry type filter support

### 10.0.2 25/03/2026
* Add XRPL error classifier with normalized `XrplErrorInfo`
* Add structured XRPL error metadata: category, subject, retryable/user-fixable flags, command, field, and warnings
* Add tests and documentation for XRPL error classification
* Minor RequestManager cleanup for pending response handling

### 10.0.1.1 24/03/2026
* Fix ErrorResponse
* Fix RippledException when error in response

### 10.0.1 20/03/2026
* Refactor gateway_balances request
* Add v1 transaction response support
* Fix test account builder
* Refactor metadata with converters for ledger types
* Add missing ledger entry request parameters
* Add wallet FromPrivateKey method
* Fix LedgerObject date conversion
* Add mnemonic verification

### 10.0.0.1-mptmeta 02/13/2026
* MPToken Metadata parser

### 10.0.0
* Upgrade to .NET 10.0
* TokenEscrow (XLS-85) — extended escrow support for fungible tokens (IOU/MPT)
* Credentials (XLS-70) — CredentialCreate, CredentialAccept, CredentialDelete transactions, LOCredential ledger entry
* PermissionedDomain (XLS-80) — PermissionedDomainSet, PermissionedDomainDelete transactions, LOPermissionedDomain ledger entry
* Permissioned DEX (XLS-81) — DomainID and tfHybrid flag for OfferCreate, DomainID for Payment

### 9.8.3-implicit 02/11/2026
* Add Currency uint implicit conversion

### 9.8.2-apiVersion 02/09/2026
* Fix API version set

### 9.8.1-connection 02/06/2026
* Connection stabilization improvements
* Minor config fix
* Documentation updates

### 9.8.0 02/04/2026
* Mnemonic wallet generator
* Xumm numbers generator
* Connection stabilization and errored tasks resolution
* Update account flags and clear flags fix
* Add test data init
* Fix connection issues

### 9.7.2 01/24/2026
* Fix race condition null exception in DID handling

### 9.7.1 01/24/2026
* Add JSON writer for converters (DID fix)

### 9.7.0 01/22/2026
* Add DID (Decentralized Identifier) support — DIDSet, DIDDelete transactions
* Add Clawback transaction support
* Add AMMClawback transaction support
* Add Oracle Set/Delete transactions (XLS-47 Price Feeds)

### 9.6.2 01/17/2026
* Add signer locator (WalletLocator) encoding
* Update connection logic
* Fix encoding issues
* Documentation updates

### 9.6.1 12/16/2025
* Add connection status tracking
* Fix namespace for BalanceChanges

### 9.6.0 12/15/2025
* Add MPToken support (MPTokenAuthorize, MPTokenIssuanceCreate, MPTokenIssuanceDestroy, MPTokenIssuanceSet)
* Add currency extensions
* Add features request

### 9.5.0 12/13/2025
* Signing refactoring — batch signing, in-batch multisign
* Refactor autofill logic
* Refactor TX common models
* Fix encoding and sign model issues
* Add sign batch tests

### 9.4.1 12/01/2025
* Add Pbkdf2 for wallet from text

### 9.4.0 11/18/2025
* Upgrade to .NET 9
* Add RequestFailurePolicy and status wait for connection
* Add reconnection stop flag and timeout for connection
* Fix on user disconnect and ping policy
* LastLedgerSequence can be null
* Refactoring and test fixes

### 9.3.0 11/12/2025
* Connection manager fix — auto-reconnect, connection ping-pong, reconnection progress

### 9.2.1 11/10/2025
* Fix Payment deliverMax serialization

### 9.2.0 11/10/2025
* Add deliverMax support
* Add warning notifications
* NFT parse update

### 9.1.5 11/09/2025
* Add destination interface

### 9.1.4 11/09/2025
* Fix ledger response

### 9.1.3 11/02/2025
* Fix WebAssembly (WASM) support error
* Add Blazor test app

### 9.1.2 10/16/2025
* Fix autofill fee calculation

### 9.1.1 10/14/2025
* Add ledger entry types
* Fix serialization error

### 9.1.0 10/14/2025
* Add Batch transaction support with multi-signature
* Add wallet from any text
* Add simulate request
* Add batch enum to base enums
* Fix flag references for in-batch TX serialization
* Update AccountInfo and AccountObjects
* Minor fixes and optimization

### 9.0.8 06/29/2025
* Add XLS-46d (dynamic NFTs) transaction support
* Fix AMM Withdraw flags
* Fix client issues

### 9.0.7 06/01/2025
* Fix NFTokenIds

### 9.0.6-beta 05/26/2025
* Fix Submit and wait logic
* Add TxV2 request/response

### 9.0.3-beta 05/24/2025
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

### 1.0.6 06/19/2022
* Fix Trustlines JsonProperty and Limit default (thanks @ReneBrauwers)

### 1.0.5 06/09/2022
* Add payment channel encoding

### 1.0.3 05/26/2022
* Update XLS-20 fields

### 1.0.2 03/31/2022
* Fix tests and initial setup

### 1.0.0 04/30/2023
* Initial Release of XrplCSharp
