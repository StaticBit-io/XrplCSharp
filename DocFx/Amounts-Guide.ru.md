# Суммы, качество и пересечение офферов

Это руководство описывает арифметику сумм XRP Ledger в SDK XrplCSharp:

- `XrplAmount` хранит сумму так же, как `STAmount` в rippled;
- `XrplAmountMath` повторяет арифметику ноды над такими суммами, включая направленное округление при пересечении офферов;
- `XrplQuality` — курс оффера;
- `OfferCrossing` рассчитывает, сколько даёт один оффер в платеже или в `OfferCreate`;
- `OfferCreateCrossing` пересекает `OfferCreate` целиком с книгами ордеров и пулами AMM, которых он достигает;
- `PaymentFlow` проводит `Payment` по его путям;
- `DexQuoteSugar` даёт котировки того и другого по леджеру ноды;
- `PathFinding` ищет пути платежа так же, как `ripple_path_find`.

Каждый результат совпадает с rippled 3.4.0 побитно: мантисса, экспонента и знак. Тесты прогоняют 8 808 векторов, полученных из собственного кода rippled, воспроизводят пересечения офферов из `AMM_test.cpp` rippled и сверяют пересечение офферов с работающей нодой.

## Содержание

- [Обзор](#обзор)
- [XrplAmount](#xrplamount)
- [Арифметика](#арифметика)
- [Качество](#качество)
- [Пересечение офферов](#пересечение-офферов)
- [Офферы пулов AMM](#офферы-пулов-amm)
- [Пересечение OfferCreate](#пересечение-offercreate)
- [Выполнение Payment](#выполнение-payment)
- [Домены permissioned DEX и credentials](#домены-permissioned-dex-и-credentials)
- [Чтение книг с ноды](#чтение-книг-с-ноды)
- [Котировки](#котировки)
- [Поиск путей](#поиск-путей)
- [Изменения балансов за пределами decimal](#изменения-балансов-за-пределами-decimal)
- [Правила леджера](#правила-леджера)

---

## Обзор

`Currency` — модель для передачи по сети: сумма в ней хранится текстом. Её `ValueAsNumber` имеет тип `decimal`: около 28 цифр в диапазоне от `1e-28` до `7.9e28`.

Выпущенная валюта (IOU) в леджере — это 16 значащих цифр и экспонента от −96 до 80, то есть значения от `1e-81` до примерно `1e96`. Код, который должен обрабатывать любую допустимую в леджере сумму или округлять так же, как нода, считает в `XrplAmount`.

| Тип | Пространство имён | Назначение |
|-----|-------------------|------------|
| `XrplAmount` | `Xrpl.Amounts` | Сумма одного актива во всём диапазоне леджера |
| `AmountKind` | `Xrpl.Amounts` | `Xrp`, `Iou` или `Mpt` — определяет, как сумма округляется |
| `XrplAmountMath` | `Xrpl.Amounts` | Арифметика `STAmount`: сложение, умножение, деление, `mulRound`, `divRound` |
| `XrplQuality` | `Xrpl.Amounts` | 64-битный курс, по которому леджер сортирует книги ордеров |
| `OfferCrossing` | `Xrpl.Amounts` | Сколько даёт один оффер при пересечении — так, как это считает `BookStep` |
| `AmmOfferMath` | `Xrpl.Amounts` | Обмены в пуле AMM и оффер, который пул выставляет рядом с книгой |
| `OfferCreateCrossing` | `Xrpl.Amounts` | Что сделает `OfferCreate` с книгами целиком и пулами AMM — по `DexSnapshot` |
| `PaymentFlow` | `Xrpl.Amounts` | Что сделает `Payment` на своих путях — по `DexSnapshot` |
| `DexQuoteSugar` | `Xrpl.Sugar` | Котировки платежа или оффера, прочитанные и рассчитанные по валидированному леджеру ноды |
| `PathFinding` | `Xrpl.Amounts` | Пути платежа, найденные так же, как их находит `ripple_path_find`, по снимку или по ноде |

## XrplAmount

`XrplAmount` всегда хранится в каноническом виде:

| Вид | Представление |
|-----|---------------|
| XRP | Целое число дропов, не больше 10^17 |
| MPT | Целое число единиц, не больше 2^63 − 1 |
| Выпущенная валюта | Мантисса в [10^15, 10^16) и экспонента в [−96, 80] |

Значение ниже диапазона становится нулём, выше — вызывает `OverflowException`.

```csharp
IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = "rIssuer..." };

XrplAmount big = XrplAmount.Parse(usd, "5e90");          // далеко за пределами decimal
XrplAmount drops = XrplAmount.Parse(new IssuedCurrency { Currency = "XRP" }, "1500000");

string text = big.ToString();                           // "5000000000000000e75" — так, как пишет rippled
XrplAmount fromWire = someCurrency.ToXrplAmount();       // из сетевой модели
Currency toWire = big.ToCurrency();                      // обратно в неё
```

`ToString` пишет то же, что `getText` в rippled:
- XRP и MPT — целым числом;
- выпущенную валюту — обычной десятичной записью, а при экспоненте меньше −25 или больше −5 — научной.

`FromNumber` превращает `XrplNumber` в сумму актива. Как и `STAmount::fromNumber`, он округляет модуль в заданном режиме, а затем возвращает знак.

## Арифметика

`XrplAmountMath` повторяет операции ноды:

| Метод | rippled |
|-------|---------|
| `Add`, `Subtract` | `STAmount + STAmount` |
| `Multiply(amount, fraction, rounding)` | `multiply(amount, frac, rm)` |
| `Multiply(v1, v2, asset)` | `multiply(v1, v2, asset)` |
| `Divide(num, den, asset)` | `divide(num, den, asset)` |
| `MulRound`, `DivRound` | `mulRound`, `divRound` |
| `MulRoundStrict`, `DivRoundStrict` | `mulRoundStrict`, `divRoundStrict` |

`MulRound` и `DivRound` при `roundUp = true` округляют от нуля, иначе — к нулю. Строгие варианты отличаются от прежних в двух местах:

- **Целочисленный результат (XRP, MPT).** `MulRoundStrict` учитывает каждую отброшенную цифру, прежний вариант — только последнюю.
- **Сборка итоговой суммы.** Строгие варианты фиксируют режим округления: для произведения — к нулю, для частного — от нуля или к нулю, как запрошено. Прежние варианты используют окружающий режим, а на ноде это округление к ближайшему. Поэтому прежний `MulRound`, которому велено округлять вниз, для выпущенной валюты может округлить к ближайшему.

```csharp
XrplAmount paid = XrplAmountMath.MulRoundStrict(limit, rate, xrp, roundUp: true);
```

## Качество

`XrplQuality` — это `Quality` из rippled, курс оффера `in / out` в 64-битной кодировке: экспонента плюс 100 в старшем байте, ниже — 16-значная мантисса.

Чем меньше закодированное значение, тем лучше качество: тейкер получает больше за меньшее. Операторы сравнения повторяют rippled: `a < b` означает, что `a` хуже.

```csharp
XrplQuality fromAmounts = XrplQuality.FromAmounts(takerPays, takerGets);
XrplQuality fromBook = XrplQuality.FromBookDirectory(offer.BookDirectory);

// Оффер из book_offers или account_objects:
XrplQuality? crossedAt = offer.BookQuality;         // по этому качеству нода его пересекает
XrplQuality remaining = offer.RemainingQuality;     // соотношение того, что в нём осталось
```

Нода пересекает оффер по качеству его директории в книге ордеров. Оно задаётся при размещении оффера и не равно соотношению того, что в оффере осталось.

`CeilIn`, `CeilOut`, `CeilInStrict` и `CeilOutStrict` урезают суммы оффера по ограничению на вход или выход при данном качестве — как `Quality::ceilIn` и `Quality::ceilOut`. `Round(digits)` — это `Quality::round`: мантисса округляется вверх, к худшему качеству, до заданного числа значащих цифр, как это делает tick size.

## Пересечение офферов

`OfferCrossing` рассчитывает один оффер так же, как `BookStep` в rippled:

1. **`Fund`** начинает с оффера: добавляет комиссию за перевод на входе, учитывает комиссию на выходе, которую платит владелец, и урезает оффер до средств владельца, если у него меньше, чем отдаёт оффер.
2. **`LimitStepOut`** урезает шаг по ограничению на выход — это обратный проход.
3. **`LimitStepIn`** урезает шаг по ограничению на вход — это прямой проход.
4. **`ForwardPass`** выполняет прямой проход по одному офферу. Если результат даёт больше, чем запросил обратный проход, при том же или меньшем входе, он, как и нода, останавливается на выходе обратного прохода.
5. **`Cross`** выполняет оба прохода для тейкера, который хочет получить `deliver` и готов заплатить не больше `sendMax`.

Остальные методы:
- `LimitIn` и `LimitOut` — это `TOffer::limitIn` и `limitOut`; `LimitIn` переключается по `fixReducedOffersV2`;
- `MulRatio` — умножение на ставку комиссии за перевод, как в типизированных суммах;
- `PassesQualityLimit` — проверка, которую результат стрэнда проходит против предельного качества тейкера.

```csharp
LedgerRules rules = await LedgerRules.FromNodeAsync(client);
XrplQuality quality = XrplQuality.FromBookDirectory(makerOffer.BookDirectory);

OfferStep funded = OfferCrossing.Fund(
    quality,
    makerOffer.TakerPays.ToXrplAmount(),
    makerOffer.TakerGets.ToXrplAmount(),
    ownerFunds: makerBalance,                       // null, если владелец сам выпускает то, что продаёт
    transferRateOut: issuerTransferRate,            // OfferCrossing.QualityOne, если комиссии нет
    rules: rules);

OfferStep step = OfferCrossing.Cross(funded, deliver: wanted, sendMax: willingToPay, rules);
// step.StepOut: что получает тейкер
// step.StepIn: что платит тейкер
// step.OwnerGives: что уходит от мейкера, с комиссией за перевод
// step.OfferIn / step.OfferOut: какая часть оффера расходуется
```

Здесь рассчитывается один оффер. Чтобы пересечь `OfferCreate` целиком, используйте [`OfferCreateCrossing`](#пересечение-offercreate).

## Офферы пулов AMM

`AmmOfferMath` даёт арифметику синтетического оффера пула AMM с округлением `Number`, как в rippled:

- `SwapIn` и `SwapOut` — это `swapAssetIn` и `swapAssetOut`: сколько пул отдаёт за вход и сколько берёт за выход.
- `ChangeSpotPriceQuality` — оффер, который доводит спотовую цену пула до заданного качества.
- `PoolOffer` — оффер, который пул выставляет рядом с книгой на одном стрэнде. При наличии книги это оффер, доводящий спотовую цену до лучшего качества книги. При `fixAMMv1_2` это самый крупный оффер пула, если он лучше книги. Без книги это 99% выхода пула. Если спотовая цена пула не лучше книги, возвращается null.

```csharp
AmmPoolOffer? offer = AmmOfferMath.PoolOffer(poolXrp, poolUsd, tradingFee, bookQuality: bestAsk, rules);
if (offer is { } pool)
{
    // pool.In / pool.Out: сколько пул берёт и отдаёт до лучшего оффера книги
    // pool.Quality: где пул стоит относительно книги
}
```

Торговая комиссия задаётся в единицах 1/100 000, то есть 1000 — это 1%. На двух стрэндах движок режет офферы пула на доли исходного пула и берёт не больше 30 офферов пула за транзакцию. Оба правила зависят от всего потока, поэтому их моделируют `OfferCreateCrossing` и `PaymentFlow`, а `PoolOffer` — нет.

## Пересечение OfferCreate

`OfferCreateCrossing.Cross` выполняет `OfferCreate` так же, как платёжный движок rippled 3.4.0, против снимка леджера `DexSnapshot`:

- **Стрэнды.** Движок пересекает прямую книгу. Если ни одна из сторон не XRP, он пересекает ещё и мост через XRP — две книги подряд — и на каждом проходе берёт стрэнд с лучшим качеством.
- **Обход книги.** Каждый проход берёт офферы одного уровня качества в порядке книги. По пути он удаляет офферы истёкшие, пустые, с глубокой заморозкой, без средств и слишком мелкие, чтобы сохранить своё качество. Владелец платит из того, что у него осталось после предыдущих переводов этой транзакции: полученное в транзакции в ней же потратить нельзя.
- **Пул AMM.** На каждом проходе пул пробуется раньше книги. На одном стрэнде его оффер рассчитывается так, чтобы цена пула сравнялась с лучшим качеством книги. На двух стрэндах пул выдаёт порции, растущие по ряду Фибоначчи, не более 30 проходов.
- **Ограничения.** Учитываются цена тейкера, `tfPassive`, `tfSell`, `tfImmediateOrCancel`, `tfFillOrKill` и tick size эмитента.
- **Проверки.** Если передана модель `OfferCreate` из SDK, сначала выполняются проверки самого оффера: некорректные офферы из `preflight` (`temINVALID_FLAG`, `temBAD_EXPIRATION`, `temBAD_SEQUENCE`, `temBAD_OFFER`, `temREDUNDANT`, `temBAD_CURRENCY`), затем из `preclaim` — `tecFROZEN`, `tecUNFUNDED_OFFER`, `tecEXPIRED`, `tecNO_ISSUER`, а также `tecNO_LINE` или `tecNO_AUTH`, если эмитент того, что просит аккаунт, требует авторизации. `OfferSequence` отменяет более старый оффер аккаунта до пересечения.
- **Авторизация.** Оффер, владельцу которого эмитент больше не разрешает держать запрошенный актив, удаляется, а не пересекается.
- **Trust lines.** Линией, которую создаёт пересечение, владеет получающий по ней аккаунт. Если баланс линии возвращается к нулю, а отправляющая сторона — к значениям по умолчанию, линия перестаёт учитываться в резерве этого аккаунта; если её не учитывает ни одна сторона, она удаляется. Оба случая перечислены в `TrustLines`.

```csharp
LedgerRules rules = await LedgerRules.FromNodeAsync(client);
OfferCreate offer = await client.Autofill(new OfferCreate
{
    Account = account,
    TakerPays = takerPays.ToCurrency(),     // что аккаунт хочет получить
    TakerGets = takerGets.ToCurrency(),     // что отдаёт
    Flags = OfferCreateFlags.tfImmediateOrCancel,
});

DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(client, offer);
OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, offer, rules);

// result.EngineResult и result.Applied: код и будет ли списана комиссия
// result.BalanceChanges: изменения балансов всех аккаунтов, как их запишут метаданные
// result.Offers: пересечённые и удалённые офферы и что от них осталось
// result.Pools: балансы пулов после транзакции
// result.TrustLines: созданные и удалённые trust lines
// result.PlacedTakerPays / PlacedTakerGets: оффер, оставшийся в книге, если он есть
```

`OfferCreateCrossing.Cross(snapshot, account, takerPays, takerGets, fee, flags, rules)` принимает тот же оффер отдельными значениями, без `OfferSequence`, `Expiration` и домена.

`OfferCreateCrossing.RoundToTickSize` даёт суммы, которые нода запишет для оффера перед пересечением. Берётся меньший из `TickSize` эмитентов тех сторон оффера, что в выпущенной валюте. Качество оффера округляется до этого числа цифр, а сдвигается неточная сторона: `TakerPays` у оффера с `tfSell`, иначе `TakerGets`. Форма может показать эти суммы до подписи оффера:

```csharp
(XrplAmount TakerPays, XrplAmount TakerGets)? written =
    OfferCreateCrossing.RoundToTickSize(takerPays, takerGets, tickSize: 5, sell: false, rules);
// null, если одна из сторон округлилась до нуля
```

`DexSnapshot.FromNodeAsync` читает всё на одном леджере: книги, которых может достичь пересечение, пулы на них, аккаунты и trust lines, которые оно читает, и оффер, который отменяет `OfferSequence`. Насколько глубоко читаются книги, описано в разделе [Чтение книг с ноды](#чтение-книг-с-ноды). Снимок можно собрать и вручную, как это делают юнит-тесты.

Результат точен относительно снимка. Транзакция попадает в более поздний леджер, состояние которого может отличаться, поэтому перед отправкой эталоном остаётся `simulate`. Не поддерживаются: книги MPT и спонсируемые резервы. Платёж рассчитывает [`PaymentFlow`](#выполнение-payment).

## Выполнение Payment

`PaymentFlow.Evaluate` выполняет `Payment` против `DexSnapshot` так же, как его применяет rippled 3.4.0. Принимается собственная модель `Payment` из SDK с её `Fee`, `SendMax`, `DeliverMin`, `Paths` и флагами.

- **Проверки.** Некорректные платежи из `preflight` (`temBAD_AMOUNT`, `temREDUNDANT`, коды `temBAD_SEND_XRP_*`, `temMALFORMED` для некорректных `CredentialIDs` или `DomainID`), проверки получателя из `preclaim` (`tecNO_DST`, `tecNO_DST_INSUF_XRP`, `tecDST_TAG_NEEDED`), предъявленные credentials и домен (см. [Домены permissioned DEX и credentials](#домены-permissioned-dex-и-credentials)), затем deposit authorization.
- **Стрэнды.** Каждый путь, а также путь по умолчанию, если не задан `tfNoDirectRipple`, становится стрэндом: отправитель, эмитент SendMax, сам путь, книга к доставляемому активу и его эмитент, получатель. Некорректный или зацикленный путь отклоняется с кодом ноды `temBAD_PATH` или `temBAD_PATH_LOOP`.
- **Trust lines.** Каждый шаг по линии учитывает, кто кому должен, `QualityIn` и `QualityOut` линии, её лимит, NoRipple, заморозки и авторизацию. Комиссия эмитента берётся там, где платёж возвращается к эмитенту, а тот выпускает дальше.
- **Книги и пулы.** Они обходятся так же, как для `OfferCreate`, но владелец оффера не платит комиссию с того, что отдаёт: её берёт со стороны отправителя следующий шаг.
- **Результат.** `SendMax`, `DeliverMin`, `tfPartialPayment` и `tfLimitQuality` определяют, сколько пройдёт, и код результата: `tecPATH_PARTIAL`, `tecPATH_DRY` или `tesSUCCESS` с доставленной суммой. Trust lines создаются и удаляются так же, как при `OfferCreate`.

```csharp
Payment payment = await client.Autofill(new Payment
{
    Account = sender,
    Destination = receiver,
    Amount = new Currency { CurrencyCode = "EUR", Issuer = eurIssuer, Value = "20" },
    SendMax = new Currency { CurrencyCode = "USD", Issuer = usdIssuer, Value = "25" },
    Paths = alternative.PathsComputed,      // из ripple_path_find
});

DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(client, payment);
PaymentFlowResult result = PaymentFlow.Evaluate(snapshot, payment, rules);

// result.EngineResult и result.Applied: код и будет ли списана комиссия
// result.DeliveredAmount: delivered_amount
// result.Paid: сколько потратил отправитель с учётом комиссий
// result.BalanceChanges, result.Offers, result.Pools: что изменил платёж
```

`DexSnapshot.FromNodeAsync(client, payment)` читает на одном леджере trust lines вдоль всех путей, которыми может пройти платёж, книги и пулы на них, аккаунты, предварительные авторизации получателя, предъявленные платежом credentials и его домен. Пути приходят вместе с платежом: из `ripple_path_find` или от локального [поиска путей](#поиск-путей). Не поддерживаются: платежи MPT и спонсируемые резервы.

## Домены permissioned DEX и credentials

`OfferCreate` или `Payment` с `DomainID` торгуют в этом permissioned-домене:

- **Членство.** Аккаунт — а для платежа и получатель — должен состоять в домене: быть его владельцем или держать credential, который домен принимает, принятый и не истёкший. Иначе результат — `tecNO_PERMISSION`; участник, у которого истёк единственный подходящий credential, получает `tecEXPIRED`.
- **Книги.** Стрэнды обходят книги домена вместо открытых: офферы домена и гибридные офферы (`tfHybrid`), которые стоят и в открытой книге. Пул AMM доменную книгу не обслуживает. Оффер, владелец которого вышел из домена — его credential истёк, — удаляется из книги домена по пути.
- **Credentials.** `CredentialIDs` платежа должны указывать на существующие credentials отправителя, принятые им (`tecBAD_CREDENTIALS`) и не истёкшие (`tecEXPIRED`). Получатель с deposit authorization пропускает платёж, если предъявленные credentials как набор пар «эмитент — тип» совпадают с одним из предварительно авторизованных им наборов.

`DexSnapshot.FromNodeAsync` читает домен, его книги и те credentials, которые домен принимает, — для участвующих аккаунтов и для каждого владельца доменного оффера. В снимке, собранном вручную, они перечисляются в `Domains` и `Credentials`, а каждый доменный оффер помечается своим `DomainId` и `Hybrid`.

## Чтение книг с ноды

По умолчанию `DexSnapshot.FromNodeAsync` читает каждую книгу одним запросом `book_offers`. Так приходят обеспеченные офферы с вершины книги: не больше 100 с публичной ноды и до 400 через admin-подключение. Книга, вернувшая полную страницу, попадает в `DexSnapshot.PartialBooks`. Если движок прошёл дальше последнего прочитанного оффера такой книги, результат сообщает об этом через `NeedsDeeperBooks`: нода может пересечь офферы, которых в снимке нет.

`DexSnapshotOptions.BookDepth` читает глубже. Загрузчик обходит директории книги так же, как движок, — уровень качества за уровнем, страницу за страницей — и читает из каждой книги до указанного числа офферов, включая необеспеченные. Поэтому офферы, которые нода удаляет по пути, тоже попадают в результат:

```csharp
DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(client, offer, new DexSnapshotOptions { BookDepth = 1_000 });
OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, offer, rules);
if (result.NeedsDeeperBooks)
{
    // Пересечение дошло дальше 1 000 офферов книги: прочитать глубже или считать результат оценкой.
}
```

Обход стоит одного запроса `ledger_entry` на оффер, поэтому он оправдан для книг, в глубину которых транзакция действительно заходит.

`DexSnapshotOptions.Ledger` задаёт леджер для чтения: номер либо `validated`, `closed` или `current`. Если он null, читается последний валидированный. `DexSnapshot.LedgerSequence` хранит леджер, на котором прочитан снимок, поэтому более глубокое чтение тех же книг можно закрепить на нём. Чтобы воспроизвести прошлую транзакцию, прочитайте леджер перед ней:

```csharp
DexSnapshot before = await DexSnapshot.FromNodeAsync(client, offer, new DexSnapshotOptions { Ledger = new LedgerIndex(transactionLedger - 1) });
```

`QuotePaymentAsync` и `QuoteOfferCreateAsync` делают каждое следующее, более глубокое чтение на леджере первого.

## Котировки

`DexQuoteSugar` читает снимок и запускает движок одним вызовом, а пока результат `NeedsDeeperBooks`, сам перечитывает книги глубже:

```csharp
// Что сделали бы оффер или платёж, если бы их применили сейчас.
OfferCrossingResult crossing = await client.QuoteOfferCreateAsync(offer);
PaymentFlowResult payment = await client.QuotePaymentAsync(existingPayment);

// Доставить ровно 20 USD: стоимость в XRP по путям, которые вернул ripple_path_find.
PaymentQuote deliver = await client.QuoteDeliverAsync(sender, receiver, XrplAmount.Parse(usd, "20"), xrp);
// deliver.Cost: сколько потратит отправитель; deliver.Payment: Amount, SendMax = Cost, Paths

// Потратить ровно 10 XRP: сколько USD это доставит.
PaymentQuote spend = await client.QuoteSpendAsync(sender, receiver, XrplAmount.Parse(xrp, "10000000"), usd);
// spend.Delivered: сколько получит получатель; spend.Payment: Amount = Delivered, SendMax = 10 XRP, Paths
```

`PaymentQuote.Payment` готов к autofill, подписи и отправке: при неизменном леджере он доставит ровно то, что показала котировка. Чтобы оставить запас на движение рынка, увеличьте его `SendMax` или добавьте `tfPartialPayment` с `DeliverMin`.


## Поиск путей

`PathFinding.FindAsync` ищет пути платежа так же, как `ripple_path_find` в rippled 3.4.0. Повторяются его `PathRequest` и `Pathfinder`: таблица форм путей для каждого вида платежа, аккаунты и книги, добавляемые звено за звеном, аккаунты-кандидаты, упорядоченные по числу путей дальше, и прогон каждого законченного пути через платёжный движок с ранжированием по качеству, ликвидности и длине. Затем считается стоимость лучших путей.

Поиск идёт по `PathfindingSource`: это `DexSnapshot` или нода, прочитанная на одном леджере. Ещё ему нужны книги ордеров. Нода держит собственный индекс книг, клиент передаёт его как `IBookIndex`:

| Фабрика `BookIndex` | Что содержит | Когда использовать |
|---|---|---|
| `FromLedgerAsync` | Все директории книг и пулы AMM леджера, как их держит `OrderBookDB` в rippled; `Observe(meta)` добавляет книги, созданные следующими транзакциями | Своя нода, standalone, тестовая сеть: те же пути, что у ноды |
| `FromAssetsAsync` | Книги между заданными активами, найденные через `book_offers` и `amm_info` | Mainnet, когда нужные активы известны |
| `FromAccountsAsync` | Книги между активами, которые держат или выпускают trust lines аккаунтов | Mainnet, как догадка по отправителю и получателю |
| `FromSnapshot` | Книги офферов и пулов снимка | Снимок, собранный вручную |

```csharp
BookIndex books = await BookIndex.FromLedgerAsync(client);
PathfindingSource source = await PathfindingSource.FromNodeAsync(client, books);
PathFindResult result = await PathFinding.FindAsync(source, new PathFindRequest
{
    SourceAccount = sender,
    DestinationAccount = receiver,
    DestinationAmount = XrplAmount.Parse(usd, "20"),   // "-1": столько, сколько купит SendMax
    SourceCurrencies = new[] { xrp },                   // пусто: все активы отправителя
    SearchLevel = 2,                                    // по умолчанию на ноде; 7 - медленный поиск
}, rules);

foreach (PathFindAlternative alternative in result.Alternatives)
{
    // alternative.SourceAmount: стоимость платежа из этого актива
    // alternative.PathsComputed: Paths платежа
}

// Или одним вызовом, с книгами между активами двух аккаунтов:
PathFindResult found = await client.FindPathsAsync(request);
```

При том же леджере и тех же книгах каждая альтернатива совпадает с ответом ноды: стоимость, доставляемая сумма и пути в том же порядке. Нода выполняет поиск вне транзакции. Там проверки, которые rippled берёт из правил текущей транзакции (округление AMM по `fixAMMv1_1` и `fixAMMv1_3`, `fixReducedOffersV2`), видят свои амендменты выключенными, и локальный поиск ведёт себя так же. Поэтому платёж, собранный по результату поиска, может доставить на последнюю цифру меньше, чем обещал поиск, причём и на ноде, и локально. Порядок альтернатив и равноценных книг следует хэш-множествам ноды и может отличаться.

`QuoteDeliverAsync` и `QuoteSpendAsync` тоже принимают аргумент `books`: если он передан, пути берутся из локального поиска вместо `ripple_path_find`.

## Изменения балансов за пределами decimal

`BalanceChanges.GetBalanceChanges` считает изменения по выпущенным валютам в `XrplAmount`: разность точная, затем она один раз округляется до 16 значащих цифр к ближайшему, половина — к чётному, как в rippled.

Баланс далеко за пределами `decimal` возвращается, а не вызывает исключение.

`Value` — это текст, который пишет `STAmount::getText` в rippled. Он переходит в научную запись (16-значная мантисса и показатель), когда показатель больше -5 или меньше -25, то есть примерно от 1e11 и выше и ниже 1e-10: изменение на 123456789012 записывается как `1234567890120000e-4`. Читать значение следует через `ToXrplAmount()` или, в пределах `decimal`, через `ValueAsNumber`. Выводить `Value` как есть или разбирать через `NumberStyles.Number` нельзя:

```csharp
Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(result.Meta);
XrplAmount received = changes[holder].Single().ToXrplAmount();   // точно при любом размере
```

## Правила леджера

Арифметика зависит от действующих амендментов:
- шкала `Number` — `SingleAssetVault` / `LendingProtocol`, `fixCleanup3_2_0` и `fixCleanup3_3_0`;
- `MPTokensV2`, при котором результат в MPT считается через `Number`;
- `fixReducedOffersV2`, который включает `ceilInStrict` в `LimitIn`;
- `fixAMMv1_1`, `fixAMMv1_2` и `fixFillOrKill`, которые меняют расчёт офферов пула и обработку `tfFillOrKill` в `OfferCreateCrossing`.

`LedgerRules.FromNodeAsync(client)` читает их с ноды. Чтобы результат совпал с результатом ноды, необходимо передавать именно их. Если правила не переданы, метод использует `new LedgerRules()`: все перечисленные амендменты включены, кроме `MPTokensV2`, — независимо от того, что действует на ноде. Это соответствует леджеру со всеми этими амендментами и может не соответствовать вашему.
