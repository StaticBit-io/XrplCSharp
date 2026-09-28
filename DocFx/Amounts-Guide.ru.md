# Суммы, качество и пересечение офферов

Это руководство описывает арифметику сумм XRP Ledger в SDK XrplCSharp:

- `XrplAmount` хранит сумму так же, как `STAmount` в rippled;
- `XrplAmountMath` повторяет арифметику ноды над такими суммами, включая направленное округление при пересечении офферов;
- `XrplQuality` — курс оффера;
- `OfferCrossing` рассчитывает, сколько даёт один оффер в платеже или в `OfferCreate`.

Каждый результат совпадает с rippled 3.4.0 побитно: мантисса, экспонента и знак. Тесты прогоняют 8 808 векторов, полученных из собственного кода rippled, и сверяют пересечение офферов с работающей нодой.

## Содержание

- [Обзор](#обзор)
- [XrplAmount](#xrplamount)
- [Арифметика](#арифметика)
- [Качество](#качество)
- [Пересечение офферов](#пересечение-офферов)
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

`CeilIn`, `CeilOut`, `CeilInStrict` и `CeilOutStrict` урезают суммы оффера по ограничению на вход или выход при данном качестве — как `Quality::ceilIn` и `Quality::ceilOut`.

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

Здесь рассчитывается один оффер. Путь через несколько офферов или книг — это по одному шагу на оффер. Стрэнд, объединяющий несколько книг или пулов AMM, рассчитывает нода; `simulate` показывает его результат без отправки транзакции.

## Изменения балансов за пределами decimal

`BalanceChanges.GetBalanceChanges` считает изменения по выпущенным валютам в `XrplAmount`: разность точная, затем она один раз округляется до 16 значащих цифр к ближайшему, половина — к чётному, как в rippled.

Баланс далеко за пределами `decimal` возвращается, а не вызывает исключение. Такие значения записываются научной записью, как у rippled:

```csharp
Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(result.Meta);
XrplAmount received = changes[holder].Single().ToXrplAmount();   // точно при любом размере
```

## Правила леджера

Арифметика зависит от действующих амендментов:
- шкала `Number` — `SingleAssetVault` / `LendingProtocol`, `fixCleanup3_2_0` и `fixCleanup3_3_0`;
- `MPTokensV2`, при котором результат в MPT считается через `Number`;
- `fixReducedOffersV2`, который включает `ceilInStrict` в `LimitIn`.

`LedgerRules.FromNodeAsync(client)` читает их с ноды. Чтобы результат совпал с результатом ноды, необходимо передавать именно их. Если правила не переданы, метод использует `new LedgerRules()`: все перечисленные амендменты включены, кроме `MPTokensV2`, — независимо от того, что действует на ноде. Это соответствует леджеру со всеми этими амендментами и может не соответствовать вашему.
