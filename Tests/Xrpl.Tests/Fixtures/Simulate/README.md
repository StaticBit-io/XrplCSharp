# simulate fixtures

`simulate` responses recorded from a standalone xrpld node, used by the unit tests of
`BalanceChanges`, `LoanPaymentOutcome`, `VaultOutcome` and the preview sugar. Each file is the
`result` object of the response, unchanged.

| File | Transaction | Node |
|---|---|---|
| `loan-pay-xrp.json` | `LoanPay` of one regular payment on a 10 XRP loan: 3 payments of 120 s, `InterestRate` 10000, `LoanServiceFee` 100 | xrpld `3.5.0~b0-1187.20260921git0229c29` (nightly pin) |
| `vault-deposit-xrp.json` | first `VaultDeposit` of 10 XRP into an empty open-ended XRP vault: the share `MPToken` is created | same |
| `vault-withdraw-xrp.json` | `VaultWithdraw` of 3 XRP by the same depositor: the share `MPToken` is modified | same |
| `loan-set-xrp.json` | `LoanSet` of a 10 XRP loan (3 payments of 120 s, `InterestRate` 10000, `LoanServiceFee` 100, `LoanOriginationFee` 1000) sent with an empty `CounterpartySignature`, before the borrower signs | xrpld `3.4.0-1` (release stand) |

The `simulate` metadata of each transaction was checked against the metadata of the same
transaction applied right after it; they were identical. For `loan-set-xrp.json` the applied
`LoanSet` carried the borrower's real signature, and the created `Loan` matched field for field.
