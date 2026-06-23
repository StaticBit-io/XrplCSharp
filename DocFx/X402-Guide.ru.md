# Руководство по агентным платежам x402

`Xrpl.X402` и `Xrpl.X402.AspNetCore` добавляют в XRP Ledger агентные платежи [x402](https://github.com/x402-foundation/x402) (поверх HTTP-402) по схеме t54 **«XRPL exact scheme»**. Они позволяют AI-агенту/клиенту автономно оплачивать HTTP-ресурс (API, инференс модели, сервис) в **XRP** или **RLUSD/IOU**, а серверу — требовать оплату за эндпоинт.

| Пакет | Роль |
|---|---|
| [Xrpl.X402](reference/Xrpl.X402.html) | **Клиент** — `DelegatingHandler`, который ловит HTTP 402, строит и локально подписывает XRPL `Payment` и повторяет запрос с заголовком `PAYMENT-SIGNATURE`. |
| [Xrpl.X402.AspNetCore](reference/Xrpl.X402.AspNetCore.html) | **Сервер** — endpoint-filter `RequirePayment` + фасилитаторы, которые верифицируют и сеттлят платёж в леджере. |

Клиент **подписывает, но не сабмитит** — транзакцию сеттлит фасилитатор мерчанта (по схеме t54). Живой interop с testnet-фасилитатором t54 подтверждён on-chain для XRP и RLUSD/IOU.

## Wire-формат (t54 XRPL exact scheme)

| Заголовок | Направление | Тело (base64-JSON) |
|---|---|---|
| `PAYMENT-REQUIRED` | сервер → клиент (на 402) | `{ x402Version, accepts: [ {scheme:"exact", network, asset, payTo, amount, maxTimeoutSeconds, extra} ] }` |
| `PAYMENT-SIGNATURE` | клиент → сервер | `{ x402Version:2, accepted, payload:{ signedTxBlob, invoiceId } }` |
| `PAYMENT-RESPONSE` | сервер → клиент | `{ success, transaction, network, payer }` |

- `asset` — это `"XRP"` (сумма в **drops**) или 40-hex код валюты (например RLUSD) с `extra.issuer` и десятичным `amount`.
- Привязка платежа к интенту делается через нативное поле `InvoiceID` (`Payment.InvoiceID = SHA-256(invoiceId)`) и/или `Memo` (`MemoData` = hex строки `invoiceId`), выбирается через [`X402IntentBinding`](reference/Xrpl.X402.X402IntentBinding.html) (по умолчанию `Both`).
- t54 энфорсит `SourceTag` — `extra.sourceTag` из требования (протокольное значение x402 `804681468`) проставляется на транзакцию.

## Использование на клиенте

```csharp
using Xrpl.Client;
using Xrpl.Wallet;
using Xrpl.X402;

IXrplClient xrpl = /* ваш подключённый IXrplClient */;
XrplWallet wallet = XrplWallet.FromSeed(seed);

// Signer делает autofill + локальную подпись (никогда не сабмитит).
var signer = new XrplWalletX402Signer(xrpl, wallet);

var options = new X402ClientOptions
{
    Network = "xrpl:1",            // CAIP-2 сеть, в которой готовы платить
    MaxAmountDrops = 10_000_000,   // жёсткий лимит для XRP-платежей (10 XRP)
    // IouValueCaps["rIssuer..."] = 5m,  // ОБЯЗАТЕЛЕН, чтобы платить IOU/RLUSD этого эмитента
};

// Оборачиваем любой HttpClient платёжным handler'ом.
var http = new HttpClient(new X402PaymentHandler(signer, options)
{
    InnerHandler = new HttpClientHandler()
});

// Прозрачно: 402 оплачивается автоматически, ресурс возвращается.
HttpResponseMessage resource = await http.GetAsync("https://api.example.com/paid");
```

### Модель безопасности (402 от мерчанта — недоверенный ввод)

- **Лимиты трат проверяются *до* подписи.** XRP ограничен `MaxAmountDrops` (проверяется всегда). **IOU/RLUSD fail-closed**: платёж отклоняется, если для эмитента нет явного лимита в `IouValueCaps`.
- **Опциональный `PayToAllowlist`** — при непустом списке и `payTo`, и IOU-`issuer` должны быть в нём.
- **Анти-двойная-оплата** — клиент платит максимум один раз на запрос; повторный 402 бросает исключение.
- **Окно валидности** — `LastLedgerSequence` ограничивается `maxTimeoutSeconds` из требования.
- Все отказы бросают [`X402PaymentException`](reference/Xrpl.X402.X402PaymentException.html) с машиночитаемым `Reason`.

## Полный флоу (XRP)

Обмен — это три HTTP-шага. `X402PaymentHandler` выполняет шаги 2–3 прозрачно внутри одного `GetAsync`; ниже — что реально идёт на проводе.

### 1. Клиент запрашивает ресурс — сервер называет цену

```http
GET /paid
→ 402 Payment Required
  PAYMENT-REQUIRED: <base64>
```

Декодированный `PAYMENT-REQUIRED`:

```json
{
  "x402Version": 2,
  "accepts": [{
    "scheme": "exact",
    "network": "xrpl:1",
    "asset": "XRP",
    "payTo": "rMerchant...",
    "amount": "1000000",
    "maxTimeoutSeconds": 600,
    "extra": { "invoiceId": "inv-123", "sourceTag": 804681468 }
  }]
}
```

### 2. Клиент платит — повторяет запрос с подписанным платежом

Handler выбирает требование `exact` на своей сети, проверяет лимиты трат, строит и локально подписывает XRPL `Payment` (1 XRP на `rMerchant`, `InvoiceID = SHA-256("inv-123")`, `SourceTag = 804681468`) и повторяет запрос с заголовком `PAYMENT-SIGNATURE`:

```http
GET /paid
  PAYMENT-SIGNATURE: <base64>
```

Декодированный `PAYMENT-SIGNATURE`:

```json
{
  "x402Version": 2,
  "accepted": { "scheme": "exact", "network": "xrpl:1", "asset": "XRP", "payTo": "rMerchant...", "amount": "1000000", "maxTimeoutSeconds": 600, "extra": { "invoiceId": "inv-123", "sourceTag": 804681468 } },
  "payload": { "signedTxBlob": "1200002280000000...", "invoiceId": "inv-123" }
}
```

### 3. Сервер сеттлит — возвращает ресурс + квитанцию

Фасилитатор верифицирует подписанный платёж и сеттлит его в леджере (ждёт `tesSUCCESS`), затем отдаёт ресурс с квитанцией `PAYMENT-RESPONSE`:

```http
200 OK
  PAYMENT-RESPONSE: <base64>

premium content
```

Декодированный `PAYMENT-RESPONSE`:

```json
{ "success": true, "transaction": "8DB5B4144A24E7D72FED584D8B0EAFFE19B9034FE5EC3DD296B19FED5731B7E8", "network": "xrpl:1", "payer": "rPayer..." }
```

### Чтение квитанции на клиенте

`GetAsync` возвращает финальный ответ `200`; декодируйте заголовок `PAYMENT-RESPONSE`, чтобы получить результат сеттлмента и хеш он-чейн транзакции:

```csharp
using System.Linq;
using Xrpl.X402.Wire;

HttpResponseMessage r = await http.GetAsync("https://api.example.com/paid");
string content = await r.Content.ReadAsStringAsync();   // "premium content"

if (r.Headers.TryGetValues(X402Headers.PaymentResponse, out var values))
{
    PaymentResponseEnvelope receipt = X402Base64Json.Decode<PaymentResponseEnvelope>(values.First());
    Console.WriteLine($"оплачено: {receipt.Success}, tx: {receipt.Transaction}, плательщик: {receipt.Payer}");
}
```

## Оплата в RLUSD / IOU

Флоу идентичен — отличается только требование, и у плательщика уже должны быть токен и trust line к эмитенту. Сервер называет цену в issued-валюте:

```json
{
  "scheme": "exact",
  "network": "xrpl:1",
  "asset": "524C555344000000000000000000000000000000",
  "payTo": "rMerchant...",
  "amount": "2.50",
  "maxTimeoutSeconds": 600,
  "extra": {
    "invoiceId": "inv-456",
    "issuer": "rIssuer...",
    "sourceTag": 804681468
  }
}
```

- `asset` — 40-hex код валюты (например RLUSD), `amount` — десятичная строка, `extra.issuer` — **обязателен**.
- Клиент строит issued-currency `Payment`, у которого `Amount` = `{ currency, issuer, value }`, и добавляет соответствующий `SendMax`; шаги 2–3 и квитанция — ровно как выше.
- IOU-платёж **fail-closed**, пока не задан лимит на эмитента:

```csharp
var options = new X402ClientOptions { Network = "xrpl:1" };
options.IouValueCaps["rIssuer..."] = 10m;   // разрешить до 10.0 токена этого эмитента
// var http = new HttpClient(new X402PaymentHandler(signer, options) { InnerHandler = new HttpClientHandler() });
```

## Использование на сервере (`Xrpl.X402.AspNetCore`)

```csharp
using Xrpl.X402.AspNetCore;
using Xrpl.X402.Wire;

IXrplClient xrpl = /* ваш подключённый IXrplClient */;
IX402Facilitator facilitator = new LedgerSettlingFacilitator(xrpl);
// либо: new T54Facilitator(new HttpClient());  // делегировать фасилитатору t54

app.MapGet("/paid", () => "premium content")
   .RequirePayment(facilitator, ctx => new PaymentRequirement
   {
       Scheme = "exact", Network = "xrpl:1", Asset = "XRP",
       PayTo = "rYourMerchantAddress...",
       Amount = "1000000",            // 1 XRP в drops
       MaxTimeoutSeconds = 600,
       Extra =
       {
           ["invoiceId"] = JsonSerializer.SerializeToElement("inv-123"),
           ["sourceTag"] = JsonSerializer.SerializeToElement(804681468)
       }
   });
```

Без `PAYMENT-SIGNATURE` эндпоинт возвращает **402** + `PAYMENT-REQUIRED`. С валидной подписью фасилитатор проверяет получателя, сеттлит транзакцию (ждёт `tesSUCCESS`), выставляет `PAYMENT-RESPONSE` и выполняет ваш handler.

Поставляются две реализации [`IX402Facilitator`](reference/Xrpl.X402.AspNetCore.IX402Facilitator.html):

- [`LedgerSettlingFacilitator`](reference/Xrpl.X402.AspNetCore.LedgerSettlingFacilitator.html) — сеттлит **локально** через ваш подключённый узел.
- [`T54Facilitator`](reference/Xrpl.X402.AspNetCore.T54Facilitator.html) — делегирует verify + settle **внешнему фасилитатору t54** по HTTP.

## Verifiable Intent

Предусмотрен passthrough: задайте `X402ClientOptions.VerifiableIntentProvider` (реализацию [`IVerifiableIntentProvider`](reference/Xrpl.X402.IVerifiableIntentProvider.html)), и его объект `extensions` прикрепляется к каждому PAYMENT-SIGNATURE под `x402Secure.verifiableIntentChain`. Полную цепочку SD-JWT L1→L3 (Mastercard Agentic / Trustline) поставляет ваш провайдер — пакет её не генерирует.

## Статус

- Поддержаны XRP и RLUSD/IOU. Реализована только схема `exact` (единственная, которую t54 анонсирует для XRPL).
- Живой interop с t54 подтверждён в testnet для XRP и RLUSD/IOU (`/verify` → `isValid:true`, `/settle` сеттлит on-chain).

См. [API-reference Xrpl.X402](reference/Xrpl.X402.html) и [API-reference Xrpl.X402.AspNetCore](reference/Xrpl.X402.AspNetCore.html) для полной документации по типам.
