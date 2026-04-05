# Changes

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
