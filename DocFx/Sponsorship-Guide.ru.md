# Гайд: Спонсируемые комиссии и резервы (XLS-68)

Как использовать XRPL Sponsored Fees & Reserves через XrplCSharp SDK. Спонсорство позволяет одному аккаунту (**спонсору**) платить комиссии транзакций и/или резервы объектов за другой аккаунт (**спонсируемого**) — базовый механизм для «безгазового» онбординга и кастодиальных сценариев.

> **Важно:** требуется амендмент `Sponsor` (XLS-68). На середину 2026 он существует только в ветке rippled `develop` — не входит ни в один релиз и не активен ни в mainnet, ни в testnet. Для экспериментов используйте nightly-стенд из этого репозитория (см. [Тестирование](#тестирование)). Фича в статусе draft и может меняться.

## Содержание

- [Обзор](#обзор)
- [Ключевые понятия](#ключевые-понятия)
- [Типы транзакций](#типы-транзакций)
- [Пошагово: создание спонсорства](#пошагово-создание-спонсорства)
- [Отправка спонсируемой транзакции](#отправка-спонсируемой-транзакции)
- [Сценарии подписания (V1/V2/V3)](#сценарии-подписания-v1v2v3)
- [Комиссии](#комиссии)
- [Объекты леджера](#объекты-леджера)
- [Тестирование](#тестирование)
- [Типичные ошибки](#типичные-ошибки)

---

## Обзор

```
Спонсор (rSponsor...)                   Спонсируемый (rUser...)
┌───────────────────────┐               ┌────────────────────────┐
│ SponsorshipSet        │──────────────►│ может слать транзакции │
│   Sponsee: rUser...   │               │   Sponsor: rSponsor... │
│   FeeAmount: 5 XRP    │               │   SponsorFlags: fee    │
│   RemainingOwnerCount │               │ комиссию платит спонсор│
└───────────────────────┘               └────────────────────────┘
        создаёт
┌───────────────────────┐
│ Sponsorship (леджер)  │
│   Owner / Sponsee     │
│   бюджет FeeAmount    │
│   RemainingOwnerCount │
└───────────────────────┘
```

Спонсируются два независимых измерения:

| Измерение | Значение `SponsorFlags` | Что платит спонсор |
|---|---|---|
| Комиссии | `SponsorCoverage.spfSponsorFee` (= 1) | `Fee` транзакций спонсируемого |
| Резервы | `SponsorCoverage.spfSponsorReserve` (= 2) | Owner-резервы создаваемых объектов |

У каждого типа транзакций появляются три общих поля: `Sponsor`, `SponsorFlags` и (когда спонсорство этого требует) `SponsorSignature` — вложенный not-signing STObject с `SigningPubKey` + `TxnSignature` спонсора поверх **того же преимиджа**, что и основная подпись.

---

## Ключевые понятия

### Объект леджера Sponsorship

Создаётся транзакцией `SponsorshipSet`, один на пару спонсор/спонсируемый:

| Поле | Значение |
|---|---|
| `Owner` | Спонсирующий аккаунт |
| `Sponsee` | Спонсируемый аккаунт |
| `FeeAmount` | Остаток XRP-бюджета на спонсируемые комиссии |
| `RemainingOwnerCount` | Сколько ещё объектов спонсор покроет резервами |

### Режим обязательной подписи

По умолчанию спонсируемый тратит бюджет без участия спонсора. Флаги `SponsorshipSet` переключают это по-измеренно:

- `tfSponsorshipSetRequireSignForFee` / `tfSponsorshipClearRequireSignForFee`
- `tfSponsorshipSetRequireSignForReserve` / `tfSponsorshipClearRequireSignForReserve`

Если подпись обязательна, транзакция спонсируемого должна нести валидный `SponsorSignature` — см. [Сценарии подписания](#сценарии-подписания-v1v2v3).

### Учётные поля

- `AccountRoot`: `SponsoredOwnerCount`, `SponsoringOwnerCount`, `SponsoringAccountCount`
- `RippleState`: `HighSponsor` / `LowSponsor` — кто покрывает резерв каждой стороны трастлайна

---

## Типы транзакций

### SponsorshipSet (отправляет спонсор)

Создаёт/обновляет отношение либо удаляет его флагом `tfDeleteObject`:

```csharp
var setup = new SponsorshipSet
{
    Account = sponsor.ClassicAddress,
    Sponsee = sponsee.ClassicAddress,
    FeeAmount = new Currency { ValueAsXrp = 5m },
    RemainingOwnerCount = 3,
};
setup = await client.Autofill(setup);
await client.SubmitAndWait(setup, sponsor, true);
```

### SponsorshipTransfer

Передаёт или завершает существующее спонсорство. Обязателен ровно один режимный флаг:

| Флаг | Кто отправляет | Дополнительные поля |
|---|---|---|
| `tfSponsorshipCreate` | текущий спонсор | `Sponsor` (новый спонсор) обязателен, `Sponsee` запрещён |
| `tfSponsorshipReassign` | текущий спонсор | `Sponsor` обязателен, `Sponsee` запрещён |
| `tfSponsorshipEnd` | спонсируемый или спонсор | `Sponsor` запрещён; `Sponsee` не должен совпадать с `Account` |

---

## Пошагово: создание спонсорства

```csharp
XrplWallet sponsor = XrplWallet.Generate();
XrplWallet sponsee = XrplWallet.Generate();
// оба кошелька предварительно пополнить

var tx = new SponsorshipSet
{
    Account = sponsor.ClassicAddress,
    Sponsee = sponsee.ClassicAddress,
    FeeAmount = new Currency { ValueAsXrp = 5m },
    RemainingOwnerCount = 3,
};
tx = await client.Autofill(tx);
TransactionSummary result = await client.SubmitAndWait(tx, sponsor, true);
// result.Meta.TransactionResult == "tesSUCCESS"
```

Проверка через `account_objects`:

```csharp
var request = new AccountObjectsRequest(sponsor.ClassicAddress) { Type = LedgerEntryType.Sponsorship };
var objects = await client.AccountObjects(request);
LOSponsorship sponsorship = objects.AccountObjectList.OfType<LOSponsorship>().First();
```

---

## Отправка спонсируемой транзакции

Спонсируемый отправляет любую обычную транзакцию с полями спонсорства:

```csharp
var payment = new Payment
{
    Account = sponsee.ClassicAddress,
    Destination = destination.ClassicAddress,
    Amount = new Currency { ValueAsXrp = 1m },
    Sponsor = sponsor.ClassicAddress,
    SponsorFlags = SponsorCoverage.spfSponsorFee,
};
payment = await client.Autofill(payment);
```

Если спонсорство **не** требует co-подписи — подписывайте и отправляйте как обычно. Если требует — один из сценариев ниже.

---

## Сценарии подписания (V1/V2/V3)

`SponsorSignature` подписывается над тем же преимиджем, что и основная подпись (аналогично counterparty-паттерну LoanSet). Три сценария через `SponsorSigningHelper`:

**V1 — Автоматический (оба ключа в одном процессе):**

```csharp
JsonObject prepared = SponsorSigningHelper.PrepareForSigning(payment, sponseeWallet);
SignatureResult signed = SponsorSigningHelper.SignSponsored(prepared, sponseeWallet, sponsorWallet);
await client.SubmitRequest(signed.TxBlob, true);
```

**V2 — Параллельный (ключи на разных устройствах, объединение позже):**

```csharp
var sponsorSig  = sponsorWallet.SignAsSponsor(prepared);
var submitterSig = sponseeWallet.Sign(prepared);
var combined = SponsorSigningHelper.CombineSponsorSignatures(submitterSig.TxBlob, sponsorSig.TxBlob);
```

**V3 — Последовательный (сначала спонсор, затем отправитель финализирует):**

```csharp
var withSponsor = sponsorWallet.SignAsSponsor(prepared);
var final = SponsorSigningHelper.SubmitterSign(withSponsor.TxBlob, sponseeWallet);
```

---

## Комиссии

По rippled `Transactor::calculateBaseFee` **одиночная** `SponsorSignature` не добавляет к базовой комиссии ничего. Надбавка возникает только при **мультисиге** спонсора: каждый подписант, вложенный в `SponsorSignature.Signers`, добавляет одну базовую комиссию. `Autofill` SDK учитывает это автоматически.

---

## Объекты леджера

- `LOSponsorship` — объект отношения (см. [Ключевые понятия](#ключевые-понятия))
- `LOAccountRoot.SponsoredOwnerCount` / `SponsoringOwnerCount` / `SponsoringAccountCount`
- `LORippleState.HighSponsor` / `LowSponsor`

---

## Тестирование

Амендмент есть только на develop. Репозиторий содержит nightly-стенд со включённым на генезисе `Sponsor`:

```bash
docker compose -f .ci-config/docker-compose.batchv11.yml up -d --build
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestISponsorship"
```

Интеграционные тесты защищены `AmendmentGuard`: на ноде без амендмента они завершаются как *inconclusive*, а не падают. Полные рабочие примеры — `Tests/Xrpl.Tests/Integration/transactions/TestISponsorship.cs`; детали стендов — [гайд по standalone-ноде](StandaloneNode-Guide.ru.md).

---

## Типичные ошибки

| Ошибка | Значение |
|---|---|
| `temDISABLED` | Амендмент `Sponsor` не активен на ноде |
| `tecNO_SPONSOR_PERMISSION` | Спонсорство не покрывает это измерение, бюджет исчерпан либо отсутствует/невалиден обязательный `SponsorSignature` |
| `terNO_PERMISSION` | Состояние отношения не допускает операцию (например, передача чужого спонсорства) |
| Валидация: "invalid Sponsor" | Поле `Sponsor` — не строковый адрес (клиентская проверка) |

*English version: [Sponsorship-Guide](Sponsorship-Guide.md)*
