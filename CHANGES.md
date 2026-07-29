# Changes

## 10.10.0.0 07/29/2026
* **`ConnectionOptions.authorization` did nothing — now it does** — the option was public on `XrplClient.ClientOptions` since the xrpl.js port, but `Connection.CreateWebSocket` was a block of commented-out JS pseudocode ending in `WebSocketClient.Create(url); // todo add options`, and `WebSocketClient` had no parameter to receive them. Nothing the caller set on `authorization`, `headers`, `proxy`, `trustedCertificates`, `key`, `passphrase` or `certificate` ever reached the socket:
  * `authorization` now produces `Authorization: Basic base64(value)` on the WebSocket upgrade handshake, matching xrpl.js `createWebSocket` — the value is the raw `user:password` pair, the SDK does the base64
  * `headers` are put on the handshake as-is; the type changed from `Dictionary<string, object>` to `Dictionary<string, string>` to match xrpl.js and drop the `ToString()` ambiguity (**source-breaking**, but the property was inert, so no working code can depend on it)
  * both are skipped under WebAssembly — the browser WebSocket API cannot set request headers, so `ClientWebSocket.Options.SetRequestHeader` is guarded by `OperatingSystem.IsBrowser()` the same way `KeepAliveInterval` already was
  * `proxy`, `proxyAuthorization`, `trustedCertificates`, `key`, `passphrase`, `certificate` are marked `[Obsolete]` rather than implemented: current xrpl.js has dropped these options too, and they cannot be honored uniformly across `ClientWebSocket` targets. They stay compiling but now say so
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
