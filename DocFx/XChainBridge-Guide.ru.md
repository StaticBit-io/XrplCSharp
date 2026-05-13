# Cross-Chain Bridge (XLS-38d)

Руководство по использованию межцепочечных мостов XRP Ledger через XrplCSharp SDK. Мосты позволяют передавать XRP или IOU-токены между двумя цепочками XRPL.

## Содержание

- [Обзор](#обзор)
- [Ключевые концепции](#ключевые-концепции)
- [Типы мостов](#типы-мостов)
- [Типы транзакций](#типы-транзакций)
- [XRP-XRP мост пошагово](#xrp-xrp-мост-пошагово)
- [IOU-IOU мост пошагово](#iou-iou-мост-пошагово)
- [Witness-серверы и аттестации](#witness-серверы-и-аттестации)
- [Ledger-объекты](#ledger-объекты)
- [Типичные ошибки](#типичные-ошибки)
- [Лучшие практики](#лучшие-практики)

---

## Обзор

Межцепочечный мост соединяет две цепочки XRPL:

- **Locking Chain** (блокирующая) — цепочка, на которой средства блокируются (хранятся у door-аккаунта)
- **Issuing Chain** (выпускающая) — цепочка, на которой выпускается эквивалентная стоимость

При переводе с locking на issuing: оригинальные средства блокируются, а на выпускающей цепочке создаётся обёрнутый эквивалент. Обратный процесс сжигает обёрнутые средства и разблокирует оригинальные.

### Архитектура

```
Locking Chain                           Issuing Chain
┌──────────────────┐                   ┌──────────────────┐
│                  │                   │                  │
│  User ──► Door   │   Аттестации     │   Door ──► User  │
│  (блокировка)    │ ◄──────────────► │  (выпуск)        │
│                  │  Witness Server   │                  │
└──────────────────┘                   └──────────────────┘
```

---

## Ключевые концепции

### Door-аккаунты

У каждого моста есть два **door-аккаунта** — по одному на каждой цепочке. Door-аккаунт выступает как хранитель, который удерживает заблокированные средства (locking-сторона) или выпускает/сжигает обёрнутые средства (issuing-сторона).

### Определение моста (`XChainBridgeModel`)

Мост уникально идентифицируется четырьмя полями, которые должны быть **абсолютно идентичны** во всех транзакциях:

```csharp
using Xrpl.Models.Common;
using static Xrpl.Models.Common.Common;

var bridge = new XChainBridgeModel
{
    LockingChainDoor = "rLockingDoorAddress",
    LockingChainIssue = new IssuedCurrency { Currency = "XRP" },
    IssuingChainDoor = "rIssuingDoorAddress",
    IssuingChainIssue = new IssuedCurrency { Currency = "XRP" },
};
```

> **Критично:** Любое расхождение в определении моста между транзакциями приведёт к ошибке. Объект моста должен быть побитово идентичен во всех транзакциях.

### Witness-серверы

Witness-серверы мониторят обе цепочки и предоставляют **аттестации** — криптографические доказательства, что транзакция произошла на одной цепочке, позволяя выполнить действие на другой. Для работы моста необходим один или несколько witness-серверов, настроенных как подписанты door-аккаунтов.

### Claim ID

**Claim ID** — уникальный идентификатор, выделяемый перед межцепочечным переводом. Он отслеживает перевод и связывает аттестации с конкретной операцией.

### Signature Reward

`SignatureReward` — вознаграждение witness-серверам за предоставление аттестаций. **Всегда указывается в XRP** (drops), независимо от типа моста.

---

## Типы мостов

### XRP-XRP мост

Переводит нативный XRP между цепочками.

**Правила:**
- `LockingChainIssue` и `IssuingChainIssue` — оба `{"currency": "XRP"}`
- `IssuingChainDoor` **обязан быть genesis-аккаунтом** на выпускающей цепочке (`rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh` для standalone/testnet)

```csharp
var bridge = new XChainBridgeModel
{
    LockingChainDoor = walletDoor.ClassicAddress,
    LockingChainIssue = new IssuedCurrency { Currency = "XRP" },
    IssuingChainDoor = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
    IssuingChainIssue = new IssuedCurrency { Currency = "XRP" },
};
```

### IOU-IOU мост

Переводит выпущенные токены (IOU) между цепочками.

**Правила:**
- `LockingChainIssue` и `IssuingChainIssue` — объекты с `currency` и `issuer`
- На locking-стороне door и issuer **могут быть разными** аккаунтами
- На issuing-стороне **`IssuingChainDoor` обязан равняться `IssuingChainIssue.issuer`** — door-аккаунт сам является эмитентом токена
- Locking door нуждается в TrustLine к locking issuer
- У locking issuer должен быть включён `DefaultRipple` (если нужны переводы между третьими аккаунтами)

```csharp
var bridge = new XChainBridgeModel
{
    LockingChainDoor = walletLockingDoor.ClassicAddress,
    LockingChainIssue = new IssuedCurrency
    {
        Currency = "USD",
        Issuer = walletLockingIssuer.ClassicAddress
    },
    IssuingChainDoor = walletIssuingDoor.ClassicAddress,
    IssuingChainIssue = new IssuedCurrency
    {
        Currency = "USD",
        Issuer = walletIssuingDoor.ClassicAddress  // ОБЯЗАН равняться IssuingChainDoor
    },
};
```

---

## Типы транзакций

| Транзакция | Назначение | Кто отправляет |
|-----------|-----------|----------------|
| `XChainCreateBridge` | Создание нового моста | Door-аккаунт |
| `XChainModifyBridge` | Обновление SignatureReward или MinAccountCreateAmount | Door-аккаунт |
| `XChainCreateClaimID` | Выделение Claim ID для перевода | Любой аккаунт |
| `XChainCommit` | Блокировка средств на исходной цепочке | Пользователь |
| `XChainClaim` | Получение средств на целевой цепочке | Пользователь (с аттестациями) |
| `XChainAccountCreateCommit` | Создание нового аккаунта на целевой цепочке | Пользователь |
| `XChainAddClaimAttestation` | Отправка аттестации witness-сервером для commit | Witness-сервер |
| `XChainAddAccountCreateAttestation` | Отправка аттестации для создания аккаунта | Witness-сервер |

---

## XRP-XRP мост пошагово

### 1. Создание моста

Door-аккаунт отправляет `XChainCreateBridge` для регистрации моста в леджере:

```csharp
using Xrpl.Models.Transactions;
using Xrpl.Models.Common;
using Xrpl.Sugar;

XChainCreateBridge createBridge = new XChainCreateBridge
{
    Account = walletDoor.ClassicAddress,
    XChainBridge = bridge,
    SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },       // 100 drops
    MinAccountCreateAmount = new Currency { Value = "10000000", CurrencyCode = "XRP" }, // 10 XRP
};
createBridge = await client.Autofill(createBridge);
TransactionSummary result = await client.SubmitAndWait(createBridge, walletDoor, true);
```

- `SignatureReward` — drops XRP, выплачиваемые witness-серверам за аттестацию
- `MinAccountCreateAmount` — минимум XRP для `XChainAccountCreateCommit` (опционально)

### 2. Создание Claim ID

Перед переводом пользователь должен выделить Claim ID:

```csharp
XChainCreateClaimID createClaimId = new XChainCreateClaimID
{
    Account = walletUser.ClassicAddress,
    XChainBridge = bridge,
    SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
    OtherChainSource = walletUser.ClassicAddress,  // аккаунт-источник на другой цепочке
};
createClaimId = await client.Autofill(createClaimId);
TransactionSummary result = await client.SubmitAndWait(createClaimId, walletUser, true);
```

### 3. Commit (блокировка на исходной цепочке)

Пользователь коммитит XRP в мост. Средства блокируются на locking chain:

```csharp
XChainCommit commit = new XChainCommit
{
    Account = walletUser.ClassicAddress,
    XChainBridge = bridge,
    XChainClaimID = "1",     // Claim ID из шага 2
    Amount = new Currency { Value = "1000000", CurrencyCode = "XRP" },  // 1 XRP
    OtherChainDestination = destinationAddress,  // опционально: получатель на другой цепочке
};
commit = await client.Autofill(commit);
TransactionSummary result = await client.SubmitAndWait(commit, walletUser, true);
```

### 4. Аттестация witness-сервером

Witness-серверы наблюдают commit на locking chain и отправляют аттестации на issuing chain:

```csharp
XChainAddClaimAttestation attestation = new XChainAddClaimAttestation
{
    Account = witnessAccount.ClassicAddress,
    XChainBridge = bridge,
    XChainClaimID = "1",
    Amount = new Currency { Value = "1000000", CurrencyCode = "XRP" },
    OtherChainSource = walletUser.ClassicAddress,
    AttestationSignerAccount = witnessAccount.ClassicAddress,
    AttestationRewardAccount = witnessAccount.ClassicAddress,
    PublicKey = witnessPublicKeyHex,
    Signature = attestationSignatureHex,
    WasLockingChainSend = 1,  // 1 = locking chain, 0 = issuing chain
    Destination = destinationAddress,
};
attestation = await client.Autofill(attestation);
TransactionSummary result = await client.SubmitAndWait(attestation, witnessAccount, true);
```

### 5. Claim (получение на целевой цепочке)

После накопления достаточного количества аттестаций пользователь получает средства на issuing chain:

```csharp
XChainClaim claim = new XChainClaim
{
    Account = walletUser.ClassicAddress,
    XChainBridge = bridge,
    XChainClaimID = "1",
    Destination = walletUser.ClassicAddress,
    Amount = new Currency { Value = "1000000", CurrencyCode = "XRP" },
};
claim = await client.Autofill(claim);
TransactionSummary result = await client.SubmitAndWait(claim, walletUser, true);
```

> **Примечание:** Если commit содержал `OtherChainDestination` и собрано достаточно аттестаций, средства могут быть доставлены автоматически без явного `XChainClaim`.

### 6. Модификация моста (опционально)

Door-аккаунт может обновить параметры моста:

```csharp
XChainModifyBridge modify = new XChainModifyBridge
{
    Account = walletDoor.ClassicAddress,
    XChainBridge = bridge,
    SignatureReward = new Currency { Value = "200", CurrencyCode = "XRP" },
};
modify = await client.Autofill(modify);
TransactionSummary result = await client.SubmitAndWait(modify, walletDoor, true);
```

### 7. Создание аккаунта на целевой цепочке (опционально)

```csharp
XChainAccountCreateCommit accountCreate = new XChainAccountCreateCommit
{
    Account = walletUser.ClassicAddress,
    XChainBridge = bridge,
    Destination = newAccountAddress,
    Amount = new Currency { Value = "20000000", CurrencyCode = "XRP" },     // 20 XRP
    SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
};
accountCreate = await client.Autofill(accountCreate);
TransactionSummary result = await client.SubmitAndWait(accountCreate, walletUser, true);
```

Мост должен иметь `MinAccountCreateAmount`, а `Amount` >= `MinAccountCreateAmount`.

---

## IOU-IOU мост пошагово

IOU-мосты требуют дополнительной настройки.

### Предварительные условия

#### 1. Включение DefaultRipple на эмитенте

Эмитент должен разрешить rippling между третьими аккаунтами:

```csharp
using Xrpl.Models.Transactions;

AccountSet enableRipple = new AccountSet
{
    Account = walletLockingIssuer.ClassicAddress,
    SetFlag = AccountSetAsfFlags.asfDefaultRipple,
};
enableRipple = await client.Autofill(enableRipple);
await client.SubmitAndWait(enableRipple, walletLockingIssuer, true);
```

> **Важно:** `DefaultRipple` должен быть включён **до** создания TrustLine. TrustLine наследуют состояние NoRipple от флага `DefaultRipple` эмитента на момент создания.

#### 2. Создание TrustLine

Locking door нуждается в TrustLine к эмитенту:

```csharp
TrustSet trustSet = new TrustSet
{
    Account = walletLockingDoor.ClassicAddress,
    LimitAmount = new Currency
    {
        CurrencyCode = "USD",
        Issuer = walletLockingIssuer.ClassicAddress,
        Value = "10000000",
    }
};
trustSet = await client.Autofill(trustSet);
await client.SubmitAndWait(trustSet, walletLockingDoor, true);
```

Пользователям для commit IOU-токенов тоже нужны TrustLine:

```csharp
TrustSet userTrust = new TrustSet
{
    Account = walletUser.ClassicAddress,
    LimitAmount = new Currency
    {
        CurrencyCode = "USD",
        Issuer = walletLockingIssuer.ClassicAddress,
        Value = "10000000",
    }
};
userTrust = await client.Autofill(userTrust);
await client.SubmitAndWait(userTrust, walletUser, true);
```

> **Примечание:** Issuing door **не** нуждается в TrustLine к самому себе — он и есть эмитент токена на issuing chain.

#### 3. Выпуск токенов пользователю

Перед commit пользователь должен иметь баланс:

```csharp
Payment issueTokens = new Payment
{
    Account = walletLockingIssuer.ClassicAddress,
    Destination = walletUser.ClassicAddress,
    Amount = new Currency
    {
        CurrencyCode = "USD",
        Issuer = walletLockingIssuer.ClassicAddress,
        Value = "1000",
    },
};
issueTokens = await client.Autofill(issueTokens);
await client.SubmitAndWait(issueTokens, walletLockingIssuer, true);
```

### Создание IOU-моста

```csharp
XChainBridgeModel bridge = new XChainBridgeModel
{
    LockingChainDoor = walletLockingDoor.ClassicAddress,
    LockingChainIssue = new IssuedCurrency
    {
        Currency = "USD",
        Issuer = walletLockingIssuer.ClassicAddress
    },
    IssuingChainDoor = walletIssuingDoor.ClassicAddress,
    IssuingChainIssue = new IssuedCurrency
    {
        Currency = "USD",
        Issuer = walletIssuingDoor.ClassicAddress  // ОБЯЗАН равняться IssuingChainDoor
    },
};

XChainCreateBridge createBridge = new XChainCreateBridge
{
    Account = walletLockingDoor.ClassicAddress,
    XChainBridge = bridge,
    SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
};
createBridge = await client.Autofill(createBridge);
await client.SubmitAndWait(createBridge, walletLockingDoor, true);
```

### Commit IOU-токенов

Поток аналогичен XRP, но `Amount` — IOU-объект:

```csharp
XChainCommit commit = new XChainCommit
{
    Account = walletUser.ClassicAddress,
    XChainBridge = bridge,
    XChainClaimID = "1",
    Amount = new Currency
    {
        CurrencyCode = "USD",
        Issuer = walletLockingIssuer.ClassicAddress,
        Value = "100",
    },
    OtherChainDestination = destinationAddress,
};
commit = await client.Autofill(commit);
await client.SubmitAndWait(commit, walletUser, true);
```

---

## Witness-серверы и аттестации

Witness-серверы необходимы для работы мостов. Они:

1. Мониторят транзакции на обеих цепочках
2. Проверяют, что commit/account create произошёл
3. Отправляют аттестации на другую цепочку

### Настройка SignerList

Door-аккаунты должны настроить `SignerList`, включающий аккаунты witness-серверов. Кворум определяет, сколько аттестаций необходимо.

### Транзакции аттестации

| Транзакция | Подтверждает |
|-----------|-------------|
| `XChainAddClaimAttestation` | `XChainCommit` на другой цепочке |
| `XChainAddAccountCreateAttestation` | `XChainAccountCreateCommit` на другой цепочке |

### Ключевые поля

| Поле | Описание |
|------|----------|
| `AttestationSignerAccount` | Аккаунт witness (должен быть в signer list door-а) |
| `AttestationRewardAccount` | Аккаунт для получения доли signature reward |
| `PublicKey` | Hex-кодированный публичный ключ witness |
| `Signature` | Hex-кодированная подпись аттестации |
| `WasLockingChainSend` | `1` — событие на locking chain, `0` — на issuing chain |

---

## Ledger-объекты

Мосты создают следующие объекты в леджере:

| Объект | Описание | Создаётся транзакцией |
|--------|----------|----------------------|
| `Bridge` | Определение моста, принадлежит door-аккаунту | `XChainCreateBridge` |
| `XChainOwnedClaimID` | Claim ID для отслеживания перевода | `XChainCreateClaimID` |
| `XChainOwnedCreateAccountClaimID` | Отслеживание создания аккаунта | `XChainAccountCreateCommit` |

### Запрос состояния моста

Через `account_objects` можно получить bridge-объекты door-аккаунта:

```csharp
using Xrpl.Models.Methods;

var request = new AccountObjectsRequest(walletDoor.ClassicAddress);
var response = await client.AccountObjects(request);

foreach (var obj in response.AccountObjectList)
{
    Console.WriteLine($"Type: {obj.LedgerEntryType}");
}
```

---

## Типичные ошибки

| Код ошибки | Причина | Решение |
|-----------|---------|---------|
| `temXCHAIN_BRIDGE_BAD_ISSUES` | Некорректное определение моста | Проверьте все 4 поля. XRP: IssuingChainDoor = genesis. IOU: IssuingChainDoor == IssuingChainIssue.issuer |
| `tecXCHAIN_NO_CLAIM_ID` | Claim ID не существует | Создайте Claim ID перед commit |
| `tecNO_PERMISSION` | Аккаунт не является door | Только door-аккаунт может создавать/модифицировать мосты |
| `terNO_RIPPLE` | Rippling не включён | Включите `DefaultRipple` на эмитенте IOU до создания TrustLine |
| `tecUNFUNDED` | Недостаточный баланс | Убедитесь, что аккаунт имеет достаточно средств |
| `tecXCHAIN_BAD_CLAIM_ID` | Неверный Claim ID | Проверьте, что Claim ID существует |

### Чеклист `temXCHAIN_BRIDGE_BAD_ISSUES`

Самая частая ошибка. Проверьте:

1. **XRP-мост:** `IssuingChainDoor` = genesis (`rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh`)
2. **XRP-мост:** Оба `Issue` = `{"currency": "XRP"}` (без поля issuer)
3. **IOU-мост:** `IssuingChainDoor` == `IssuingChainIssue.Issuer`
4. **IOU-мост:** Оба Issue-поля содержат `currency` И `issuer`
5. **Все мосты:** Объект `XChainBridge` **абсолютно идентичен** во всех транзакциях

### Чеклист `terNO_RIPPLE`

1. Вызовите `AccountSet` с `SetFlag = AccountSetAsfFlags.asfDefaultRipple` на эмитенте
2. Сделайте это **до** создания TrustLine (TrustLine наследуют флаг в момент создания)
3. Убедитесь, что на TrustLine не установлен `NoRipple` явно

---

## Лучшие практики

1. **Храните определение моста один раз** — создайте `XChainBridgeModel` единожды и используйте во всех транзакциях. Любое различие в полях приведёт к ошибке.

2. **Используйте константы для door-адресов** — особенно genesis для XRP-мостов:
   ```csharp
   const string GenesisAccount = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
   ```

3. **Включайте DefaultRipple заранее** — для IOU-мостов включите на эмитенте до создания любых TrustLine.

4. **SignatureReward — всегда XRP** — независимо от типа моста.

5. **MinAccountCreateAmount** — задавайте только для XRP-мостов, где нужно создание аккаунтов через мост.

6. **Проверяйте результаты** — всегда проверяйте `TransactionResult`:
   ```csharp
   if (result.Meta?.TransactionResult != "tesSUCCESS")
       throw new Exception($"Transaction failed: {result.Meta?.TransactionResult}");
   ```

7. **Безопасность witness** — в продакшене используйте multi-signature signer list с кворумом > 1. Никогда не полагайтесь на одного witness.

8. **Формат Amount:**
   - XRP: `new Currency { Value = "1000000", CurrencyCode = "XRP" }` (значение в drops)
   - IOU: `new Currency { CurrencyCode = "USD", Issuer = "rAddress", Value = "100" }` (десятичное значение)

9. **Тестирование на standalone** — amendment `XChainBridge` должен быть включён:
   ```json
   { "command": "feature", "feature": "XChainBridge", "vetoed": false }
   ```

---

## Ссылки

- [Спецификация XLS-38d](https://github.com/XRPLF/XRPL-Standards/tree/master/XLS-0038d-cross-chain-bridge)
- [Документация XRPL: Cross-Chain Bridges](https://xrpl.org/docs/concepts/interoperability/cross-chain-bridges)
- [Справочник транзакций XChainBridge](https://xrpl.org/docs/references/protocol/transactions/types/xchaincreatebridge)
