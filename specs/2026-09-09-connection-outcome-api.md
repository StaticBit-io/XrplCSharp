# Результат перехода соединения читается типом, а не текстом

Дата: 2026-09-09. Статус: **реализовано**, ревизия 4. Один PR, один релиз.
Базис: 11.4.0.0 (после #181).

**Открытых вопросов нет.** Всё, что относится к теме, решено здесь и едет одним изменением:
типы исключений, свипы запросов в полёте (2.6), незащищённый `SetNetworkId` на пути
`ChangeServer` (2.7), событийное ожидание готовности (3.2) и причина останова в потоке статуса
(4). Возвращаться к этому коду второй раз не потребуется.

Источник: запрос от двух команд, использующих этот SDK. Запрос принят по существу;
расхождения с ним перечислены в разделе 6.

> **Ревизия 2.** Первая редакция прошла два независимых ревью (Fable 5.1 с полным доступом к
> файлам, Codex по выдержкам без доступа к репозиторию). Оба нашли одно и то же ядро проблем,
> и все находки проверены по коду перед внесением. Что изменилось:
> - раздел 2.3 первой редакции **сужал** внутреннюю проверку на `connection.cs:1339` до двух
>   новых типов. Это вернуло бы дефект «вторая серия переподключения», исправленный в 11.4.0.0:
>   `WaitForConnectionAsync` бросает и базовый `NotConnectedException` — через
>   `CheckIfNotConnected()` на входе. Сужение убрано, раздел переписан на обратное утверждение;
> - «инвариант 1:1» между перечислением исходов и типами исключений был ложен в обе стороны.
>   Заменён областью действия с полным перечислением (раздел 3.1);
> - `ClientDisconnectedException` смешивал пользовательское отключение с внутренним, которым
>   заканчивается отказ на `OnConnected`-обработчике. В репозитории уже есть два теста на один
>   сценарий, которые под первой редакцией дали бы разные типы в зависимости от тайминга.
>   Введена причина отключения (раздел 2.3);
> - `ReconnectInfo.Exhausted` заменён на `ConnectionStatusInfo.StopReason`: признак ставится на
>   объект, описывающий уведомление целиком, `Reconnect != null` сохраняет единственный смысл, и
>   этим же закрывается бывший открытый вопрос 2 (раздел 4);
> - `HasConnectionAsync` на интерфейс не поднимается, перегрузка с `CancellationToken` не
>   добавляется: она делает вызов без аргументов неоднозначным (CS0121) у потребителей;
> - добавлено предусловие реализации, которого не было: у `NotConnectedException` нет
>   конструктора с `innerException`;
> - пункт 6.3 первой редакции утверждал, что `ChangeServer` не читает network id. Это неверно,
>   предложение потребителей было право — см. 1.5.
>
> **Ревизия 3.** Три открытых вопроса второй редакции закрыты решениями, а не переносом:
> - свипы запросов в полёте больше не остаются голым `OperationCanceledException` (2.6);
> - предложенный потребителями готовый примитив ожидания **не берём** (3.2);
> - `XrplClient.ChangeServer` получает ту же защиту повторами, что и `Connect` (2.7).
>
> **Ревизия 4 — по итогам реализации.** Три места, где код разошёлся со спекой, и все три
> нашлись тестами, а не рассуждением:
> - **`Task.WhenAll` подтип не теряет** — оба ревьюера утверждали обратное, и тест, написанный
>   по их формулировке, упал. Подтип теряется только рядом со сбойной задачей, и тогда отмена
>   отбрасывается совсем (2.5);
> - **переиспользовать `ConnectionManager` как сигнал готовности нельзя**: он будит ожидающих на
>   отставке соединения, а отставка обязана переносить запрос на новое соединение. Построен
>   отдельный сигнал, и правило «пробуждение всегда перевзводит сигнал» — исправление дефекта,
>   который поймал тест (3.2);
> - **фильтр повторов из 2.7 был слишком широким** дважды подряд: сначала исключал всё
>   перекрытие, потом всё, кроме реконнекта. Верная граница — между жизненным циклом самого
>   клиента и операцией-соседом (2.7).

## 0. Порядок работ

Одним PR, в этом порядке — каждый шаг опирается на предыдущий:

1. **2.0** — конструктор `NotConnectedException(string, Exception?)`. Без него не собирается 2.1.
2. **2.1, 2.3** — новые типы и причина отключения.
3. **2.2** — переключение точек броска на новые типы.
4. **2.6** — свипы запросов в полёте.
5. **2.7** — `SetNetworkId` на пути `ChangeServer`.
6. **4** — `ConnectionStopReason` в потоке статуса.
7. **3.1** — `ConnectionWaitOutcome` и члены на `Connection`, `XrplClient`, `IXrplClient`.
8. **3.2** — событийное ожидание. Идёт последним: оно опирается на типы из 2.1, на причину из
   2.3 и на свипы из 2.6, и именно оно даёт единственное изменение внутренней механики.

Тесты раздела 5 пишутся вместе с шагом, который они закрепляют, а не в конце.

## 1. Проблема

11.4.0 сделал поведение соединения правильным: у перехода один владелец, перекрытая операция
сообщает, что её перекрыли, вместо возврата успеха от сервера, который клиент покинул. Чего он
не сделал — не дал вызывающему **прочитать** этот ответ.

Сегодня «что случилось с моим соединением» выразимо только парой «тип + текст сообщения».
Тип перегружен, текст — не контракт: changelog 11.3.2.0 сам предписывает «Classify by type
rather than by message», а типов, способных на это, библиотека не даёт. Внутренне
противоречивый контракт.

### 1.1. Шесть значений `NotConnectedException`

| Место | Значение | Что должен сделать потребитель |
|---|---|---|
| `connection.cs:885`, `:1392`, `:2530` | клиент отключён — но **чем**, по типу неизвестно (см. 1.4) | зависит от причины |
| `connection.cs:1399` | цикл переподключения исчерпал бюджет попыток | этот узел не отвечает; повод для failover |
| `connection.cs:2507` | запрос отклонён сразу по `RequestFailurePolicy.ImmediateFail` | повторить после подключения; узел ни при чём |
| `connection.cs:2798` | отказ на падающем `OnConnected`-обработчике, запросы в полёте | узел отвечает, отказал наш обработчик; failover не поможет |
| `connection.cs:2542` | попытки подключения нет вовсе | вызвать `Connect()` |
| `connection.cs:2516`, `:2522`, `:2315` | остаточные ветки, см. ниже | — |

Про три остаточные ветки, чтобы они не выглядели пропущенными:

- `:2516` — проверка `ShouldBeConnected()` **после** успешного возврата из ожидания. Это не
  таймаут ожидания: ожидание уже вернулось.
- `:2522` — ветка `default` в `switch` по перечислению из двух значений. Недостижима.
- `:2315` — `connectionManager.RejectAllAwaiting(...)`, и путь мёртв: единственный потребитель
  этого механизма, `ConnectionManager.AwaitConnection`, не вызывается нигде в `Xrpl/Client`.

Все три остаются на базовом типе и в объём не входят.

### 1.2. Перекрытие неотличимо от отмены вызывающим

`SupersededLocked()` (`connection.cs:882`) уже делает `switch` по `_generationKind` и уже держит
`url` — то есть **конструирует правильный ответ и тут же теряет его** в голом
`OperationCanceledException`, где случай различим только по тексту.

Четвёртая точка, `ChangeServer` после успешного ожидания (`connection.cs:1153`), идёт мимо
`SupersededLocked` и бросает такой же голый `OperationCanceledException` от себя. Её условие —
только `connectedTo != server`; вид перехода-победителя оно не читает, поэтому текст сообщения
(«superseded by a later ChangeServer») — вывод, а не факт.

Для вызывающего всё это неотличимо от отмены собственным токеном. Потребители различали по
тексту или по внешнему флагу «я сам это отменил».

### 1.3. Ожидание готовности: опрос и недоступная форма ответа

`WaitForConnectionAsync` (`connection.cs:1361`) — цикл `Task.Delay(100 ms)`. Он медленнее
события, которого ждёт, а на Blazor WebAssembly дороже, чем выглядит: поток один, а таймеры в
скрытой вкладке троттлятся, и ожидание, которое событие удовлетворило бы мгновенно,
растягивается на секунды.

Форма «не вернулось за отведённое время» — это ответ, а не сбой, и она в библиотеке **уже
есть**: `HasConnectionAsync` (`connection.cs:1426`). Но она ловит `System.TimeoutException` и
`OperationCanceledException`, а `NotConnectedException` пропускает наружу, и не принимает
`CancellationToken`. Потребитель, держащий `IXrplClient`, добирается до обеих только через
`client.connection` (`IXrplClient.cs:85`) — то есть через утечку всего внутреннего объекта.

### 1.4. Отказ на обработчике неотличим от пользовательского отключения

Путь «сдались на `OnConnected`-обработчике» отклоняет запросы в полёте своим сообщением
(`connection.cs:2798`), а затем **сам вызывает** `await Disconnect()` (`connection.cs:2802`).
`Disconnect()` ставит `_permanentlyDisconnected` (`connection.cs:773`), и после этого каждая
точка, которая читает этот флаг, отвечает так, будто клиента выключил потребитель:
`:1392` для ожидающего, `:885` для перекрытой операции, `:2530` для следующего запроса.

Это не гипотеза: в репозитории уже есть два теста на один и тот же сценарий.
`TestUOnConnectedHandlerFailure.cs:312` — обработчик падает сразу, и `Connect()` завершается
через `:1392`. `TestUOnConnectedHandlerFailure.cs:192` — обработчик падает через 400 мс,
`Connect()` доходит до `SetNetworkId` и получает исключение из `:2798`. **Один сценарий, два
разных ответа в зависимости от тайминга** — ровно то, от чего это изменение должно избавить.

### 1.5. Ловушка с `TimeoutException` живёт на пути `ChangeServer`

`XrplClient.ChangeServer` (`IXrplClient.cs:907`) после `connection.ChangeServer` вызывает
`await SetNetworkId()` — то есть переключение сервера действительно спрашивает у нового узла
его network id, и этот запрос может завершиться `Xrpl.Client.Exceptions.TimeoutException`,
который не является `System.TimeoutException`. Предложение потребителей описало это верно.

Отдельно стоит отметить, хотя в объём это не входит: `Connect` защищает тот же вызов повторами
через `SetNetworkIdWhileConnectingAsync` (`IXrplClient.cs:965`), а `ChangeServer` вызывает
`SetNetworkId()` напрямую, без повторов. Исправляется здесь же — см. 2.7.

### 1.6. «Ещё пытаюсь» против «сдался» выводится из отсутствия

Состояние `Disconnected` рассылается из десяти мест: `connection.cs:1792`, `:1803`, `:1856`,
`:1866`, `:2291`, `:2350`, `:2774`, `:3041`, `:3072`, `:3202`. Ни одно не передаёт `reconnect:`.
Уведомление «цикл сдался» неотличимо по форме от «пользователь отключился», «первое подключение
не удалось» и «закрыто окончательно» — отличается только текст.

## 2. Решение, часть 1: типы вместо таблицы соответствий

Не «перенести метод `Classify` в SDK». Таблица соответствий — обходной приём для типов, не
различающих то, что вызывающий обязан различать. Библиотека знает, какой это случай, в момент
броска.

### 2.0. Предусловие реализации

`NotConnectedException` имеет **единственный** конструктор `(string message = null)`
(`Exceptions/XrplException.cs:89`). Наследник, который должен нести исходную ошибку, передать её
некуда: у `InnerException` нет сеттера. Поэтому первым шагом:

```csharp
public NotConnectedException(string message, Exception? innerException)
    : base(message ?? DefaultMessage, innerException) { }
```

Базовый `XrplException` такой конструктор уже имеет (`Exceptions/XrplException.cs:31`), так что
правка на одну строку — но без неё раздел 2.1 нереализуем.

### 2.1. Новые типы

```csharp
// Все — наследники NotConnectedException: существующие catch продолжают ловить.
public class ClientDisconnectedException   : NotConnectedException  // потребитель вызвал Disconnect()
public class ReconnectExhaustedException   : NotConnectedException  // цикл исчерпал бюджет попыток
public class RequestRefusedException       : NotConnectedException  // ImmediateFail отклонил запрос
public class ConnectHandlerFailedException : NotConnectedException  // отказ на OnConnected
public class NotConnectingException        : NotConnectedException  // попытки подключения нет

// Наследник OperationCanceledException — по той же причине.
public class ConnectionSupersededException : OperationCanceledException
```

Носители данных, а не только имена:

- `ReconnectExhaustedException`: `int Attempts`, `int MaxAttempts`.
  **`Attempts` — число сделанных попыток, то есть равно `MaxAttempts`.** Определить это
  обязательно: `_reconnectAttempts` инкрементируется в начале витка (`connection.cs:3187`),
  цикл останавливается при `> MaxReconnectAttempts` (`:3198`) и счётчик на выходе не сбрасывает,
  так что сырое значение в точке броска равно `MaxAttempts + 1`. Публиковать «6 из 5» нельзя.
- `ConnectHandlerFailedException`: `int Failures` и исходная ошибка во `InnerException`
  (см. 2.0). Обе величины есть на `connection.cs:2798`.
- `ConnectionSupersededException`: `string? SupersededBy` — где операция-победитель оставила
  клиента, — и `ConnectionTransitionKind Kind`:

```csharp
public enum ConnectionTransitionKind
{
    Connect,
    ChangeServer,
    Disconnect,
    Reconnect,      // FastReconnect приватного TransitionKind
}
```

`None` приватного `TransitionKind` в перечисление не входит: значение означает «перехода нет»,
и в точке броска оно недостижимо.

`Disconnect` **достижим только на свипах запросов** (2.6). На пути перекрытого перехода
`TransitionKind.Disconnect` даёт `ClientDisconnectedException`, а не
`ConnectionSupersededException`, — так это работает сегодня, и менять наблюдаемый тип там
нельзя. Асимметрия не случайная: каждая половина сохраняет тот тип, который вызывающий получает
сейчас. Это записано в XML-документации `Kind`, чтобы потребитель не искал недостижимую ветвь и
не удивлялся достижимой.

### 2.2. Точки броска

| Файл:строка | Было | Станет |
|---|---|---|
| `connection.cs:885` | `NotConnectedException` | `ClientDisconnectedException` или `ConnectHandlerFailedException` — по причине (2.3) |
| `connection.cs:886–888` | `OperationCanceledException` | `ConnectionSupersededException` (`Kind` из `_generationKind`, `SupersededBy = url`) |
| `connection.cs:1153` | `OperationCanceledException` | `ConnectionSupersededException`, `Kind` читается из `_generationKind` **под `_transitionLock`**, `SupersededBy = connectedTo` |
| `connection.cs:1392` | `NotConnectedException` | `ClientDisconnectedException` или `ConnectHandlerFailedException` — по причине (2.3) |
| `connection.cs:1399` | `NotConnectedException` | `ReconnectExhaustedException` |
| `connection.cs:2507` | `NotConnectedException` | `RequestRefusedException` |
| `connection.cs:2530` | `NotConnectedException` | `ClientDisconnectedException` или `ConnectHandlerFailedException` — по причине (2.3) |
| `connection.cs:2542` | `NotConnectedException` | `NotConnectingException` |
| `connection.cs:2798` | `NotConnectedException` | `ConnectHandlerFailedException` |

`:2516`, `:2522` и `:2315` остаются на базовом `NotConnectedException` (см. 1.1).

На `:1153` вид **читается**, а не подставляется константой: условие там доказывает только
несовпадение адресов. Сегодня по построению победителем оказывается `ChangeServer` (`Connect()`
и быстрый реконнект идут на текущий `url`, то есть дали бы `connectedTo == server`), но это
вывод из чужого кода, а не факт, и превращать его в структурированные публичные данные нельзя.

Тексты сообщений сохраняются дословно. Смысл переносится в тип, а не переписывается в строке.

### 2.3. Причина отключения

Чтобы 1.4 перестало быть правдой, отключение должно нести причину:

```csharp
private enum DisconnectCause
{
    User,                 // Disconnect() / DisconnectAndWaitAsync() от потребителя
    ConnectHandlerGaveUp, // путь connection.cs:2774-2802
}
```

Причина записывается **в той же критической секции, где ставится
`_permanentlyDisconnected`** (`connection.cs:773`), и живёт ровно столько же — отдельной логики
сброса нет, а значит нет и способа её рассинхронизировать. Практически это означает внутренний
параметр у `Disconnect()`, которым путь отказа на обработчике (`connection.cs:2802`) передаёт
`ConnectHandlerGaveUp`, а все публичные вызовы — `User` по умолчанию.

Читают её три точки — `:885`, `:1392`, `:2530` — и бросают
`ConnectHandlerFailedException` (с `Failures` и `InnerException`) вместо
`ClientDisconnectedException`, когда причина `ConnectHandlerGaveUp`.

### 2.4. Почему внутренняя проверка остаётся широкой

`connection.cs:1339` — `if (ex is NotConnectedException)` в обработчике ошибок быстрого
реконнекта — **не сужается**. Первая редакция этой спеки предлагала заменить проверку на пару
новых типов, обосновывая это тем, что «`WaitForConnectionAsync` в этой точке может бросить
только их». Это неверно: ожидание на входе вызывает `CheckIfNotConnected()`
(`connection.cs:1368`), а тот бросает базовый `NotConnectedException` на `:2542` — под этой
спекой `NotConnectingException`, всё ещё наследник базы, но не один из двух названных.

Путь достижим. При `MaxReconnectAttempts = 1`: `ConnectCoreAsync` падает на сокете →
`OnConnectionFailed` запускает цикл на том же источнике отмены → цикл инкрементирует счётчик,
получает `2 > 1`, объявляет `Disconnected` и на выходе обнуляет `_reconnectCts` и
`_reconnectLoopGeneration` (`connection.cs:3375`) → если это успевает до входа в
`WaitForConnectionAsync`, `CheckIfNotConnected` видит `ws == null && _reconnectCts == null` при
состоянии `Disconnected` и бросает `:2542`. Сегодня широкая проверка это ловит и останавливается.
Сузив её, мы отправили бы исключение в `StartReconnectLoop`, чей guard по поколению уже сброшен —
то есть запустили бы вторую серию за терминальным `Disconnected`. Это ровно тот дефект, который
закрыт в 11.4.0.0 («a fast reconnect no longer runs a second full series after the loop gave
up»).

Проверка семантически верна как есть: **любой** `NotConnectedException` из ожидания означает
«клиент не подключается, вторую серию не запускать». Комментарий на `:1335–1338` уточняется,
код не меняется.

### 2.5. Что даёт наследование от `OperationCanceledException` и чего не даёт

`AsyncTaskMethodBuilder.SetException` переводит задачу в `Canceled`, когда исключение —
`OperationCanceledException`, независимо от токена. Так ведёт себя `ChangeServer` уже сегодня;
наследник ведёт себя так же, `catch (OperationCanceledException)` продолжает ловить. Записано,
чтобы это не «чинили».

Чего наследование не даёт, и что должно попасть в XML-документацию нового типа:

- подтип сохраняется при **прямом** `await` задачи. **Уточнено замером** (`TestUConnectionOutcomeTypes`),
  и замер опроверг ожидание, с которым тест писался: `Task.WhenAll` подтип тоже сохраняет, пока
  среди задач нет сбойных — комбинированная задача хранит первое исключение отмены и бросает
  именно его. Теряется он в другом случае: если хоть одна задача **сбойная**, `WhenAll`
  записывает только сбои, а отмену отбрасывает совсем — её нет ни в `await`, ни в
  `Task.Exception.InnerExceptions`. Писать пессимистичное правило («через `WhenAll` подтип
  теряется всегда») значило бы отправить потребителя искать обходной путь, который ему не нужен;
- конструктор передаёт `CancellationToken.None`: иначе фильтры вида
  `when (ex.CancellationToken == myToken)` начнут срабатывать на перекрытии, которого
  вызывающий не отменял.
- `catch (TaskCanceledException)` новый тип не поймает — он ему не наследник. Это правильно и
  неочевидно.
- внутри SDK новый тип попадает в существующие `catch (OperationCanceledException)`:
  `connection.cs:1492` (`Connect` глотает — желаемо), `:3319` (цикл — желаемо),
  `IXrplClient.cs:977` (`SetNetworkIdWhileConnectingAsync` повторяет — желаемо). Проверено, в
  тестах закрепить.
- **`TaskCompletionSource.SetException(OperationCanceledException)` даёт `Faulted`, а не
  `Canceled`.** Правило асинхронного билдера на TCS не распространяется — это важно для этапа
  2b (3.2), где ожидание переводится на TCS.

### 2.6. Свипы запросов в полёте

Потребители из предложения держат `IXrplClient` и чаще всего сталкиваются не с перекрытым
переходом, а с **собственным запросом, который умер, пока соединение переезжало**. Такой запрос
сегодня отклоняется через `RequestManager.RejectAllWithCancellation()` — голым
`OperationCanceledException("Connection was intentionally closed.")`
(`RequestManager.cs:246`) — и это ровно тот случай, о котором changelog 11.3.2.0 пишет «or
`OperationCanceledException`, for a request that was in flight when the switch began». Оставить
его голым значит закрыть половину задачи.

**Правило.** Свип, который делает **переход**, забирающий соединение куда-то, сообщает куда.
Свип, вызванный **отказом самого соединения**, остаётся отменой — намеренно, см. ниже.

Механизм уже есть: `RequestManager.RejectAll(Exception)` (`RequestManager.cs:232`) и
`ConnectionManager.RejectAllAwaiting(Exception)` (`ConnectionManager.cs:23`) принимают готовое
исключение. Добавляется одна приватная фабрика рядом с `SupersededLocked` — тот же `switch`,
но для свипа, — и каждая точка передаёт свой вид перехода.

| Точки | Кто делает свип | Чем отклоняется |
|---|---|---|
| `:1101`, `:1102` | `ChangeServer` | `ConnectionSupersededException(ChangeServer, SupersededBy = server)` |
| `:1245`, `:1246` | быстрый реконнект | `ConnectionSupersededException(Reconnect, SupersededBy = url)` |
| `:1469` | `Connect` | `ConnectionSupersededException(Connect, SupersededBy = url)` |
| `:1780`, `:1781` | `Disconnect` | `ConnectionSupersededException(Disconnect, SupersededBy = null)` |
| `:1831`, `:1832` | `DisconnectAndWaitAsync` | `ConnectionSupersededException(Disconnect, SupersededBy = null)` |
| `:2849` | отказ обработчика, ветка повтора | `ConnectionSupersededException(Reconnect, SupersededBy = url)` |

Все шесть остаются наследниками `OperationCanceledException`, поэтому **ни один существующий
`catch (OperationCanceledException)` не перестаёт срабатывать и ни одна задача не меняет
статус**. Меняется только то, что теперь можно прочитать.

`:1780`/`:1831` намеренно дают `ConnectionSupersededException(Disconnect)`, а не
`ClientDisconnectedException`: последний — наследник `NotConnectedException`, и запрос, который
сегодня умирает отменой, начал бы умирать сбоем. Ради читаемости типа ломать это нельзя, а
`Kind = Disconnect` отвечает на вопрос полностью.

`:2849` — ветка, где обработчик `OnConnected` упал, но клиент будет пробовать снова; запрос
умер из-за пересоздания соединения, поэтому `Reconnect`, а не «обработчик сломан». Терминальная
ветка того же пути (`:2798`) остаётся `ConnectHandlerFailedException`, как в 2.2.

**Что остаётся отменой и почему.** Свипы на `:2290`/`:2291`, `:2310`/`:2311` и `:2995` вызваны
не переходом, а закрытием сокета — сетевым обрывом или закрытием, которое уже отчиталось само.
Комментарий на `:2992` фиксирует это как решение: отмена выбрана, чтобы приложения-потребители
не писали такие случаи в Critical. Решение остаётся в силе, а `StopReason` (раздел 4) даёт
причину на потоке статуса, где ей и место. `:2315` не трогается — путь мёртв (1.1).

### 2.7. `SetNetworkId` на пути `ChangeServer`

`XrplClient.Connect` защищает чтение network id повторами через
`SetNetworkIdWhileConnectingAsync` (`IXrplClient.cs:965`), потому что соединение сразу после
подключения может быть ещё неустоявшимся. `XrplClient.ChangeServer` (`IXrplClient.cs:916`)
вызывает `SetNetworkId()` напрямую. Переключение сервера подвержено ровно тому же: сокет
открылся, `WaitForConnectionAsync` вернулся, а `OnConnected`-обработчик или конкурентный
переход уже уносит соединение — и `server_info` умирает вместе с ним.

`:916` заменяется на `await SetNetworkIdWhileConnectingAsync(cancellationToken)`.

Одновременно уточняется фильтр повторов на `IXrplClient.cs:976`. Сейчас он ловит
`OperationCanceledException or DisconnectedException`; после 2.1 в `OperationCanceledException`
попадает и `ConnectionSupersededException`, а после 2.6 — ещё и свипы запросов.

**Исключать `ConnectionSupersededException` целиком нельзя** — это выяснилось прогоном
существующего теста `TestRecoveredHandlerFailureWithRequestInFlightStillConnects`, который
упал на первой редакции правки. Пересборка соединения (свип из 2.6 с
`Kind == Reconnect` — реконнект health-check'а или повтор после один раз упавшего
`OnConnected`) — это ровно тот случай, ради которого цикл повторов и существует: клиент
возвращается на тот же сервер. Перекрытие **потребительской** операцией — не тот: соединение
теперь принадлежит ей.

Фильтр решает по виду перехода:

```csharp
private static bool IsWorthAnotherNetworkIdAttempt(Exception error) =>
    error switch
    {
        ConnectionSupersededException superseded =>
            superseded.Kind == ConnectionTransitionKind.Reconnect,
        OperationCanceledException => true,
        DisconnectedException => true,
        _ => false,
    };
```

**Граница уточнена повторно, тоже прогоном тестов.** Исключать по признаку «не реконнект» тоже
неверно: путь отказа на обработчике заканчивается собственным `Disconnect()`, и запрос на
network id гибнет в его свипе — то есть под `Kind == Disconnect`. Пробросив это, `Connect()`
сообщал бы случайный свип вместо причины, по которой клиент сдался, причём **в зависимости от
тайминга**: при быстром событийном ожидании приходило одно, при медленном опросе — другое.

Верная граница — между собственным жизненным циклом клиента и операцией-соседом. `Reconnect` и
`Disconnect` переспрашиваем: ожидание внутри следующей попытки сообщит настоящую причину
(`ConnectHandlerFailedException`, `ClientDisconnectedException`). `Connect` и `ChangeServer`
пробрасываем: соединение принадлежит той операции, и «вашу операцию перекрыли» — это весь
ответ.

## 3. Решение, часть 2: ожидание готовности

### 3.1. Форма ответа и интерфейс

Вместо предложенного `Task<bool>` — перечисление. Это ровно та же мысль, что и в части 1, но на
неисключительном пути: «не вернулось за отведённое время», «сдался» и «попытки нет» — разные
ответы, и сводить их в один `false` значит воспроизвести исходную проблему в новом методе.

```csharp
public enum ConnectionWaitOutcome
{
    Connected,             // соединение установлено
    TimedOut,              // не вернулось за отведённое время
    ReconnectExhausted,    // цикл исчерпал бюджет попыток
    Disconnected,          // потребитель вызвал Disconnect()
    ConnectHandlerFailed,  // отказ на OnConnected-обработчике
    NotConnecting,         // попытки подключения нет — нужен Connect()
}

// на Connection, на XrplClient и на IXrplClient (default-реализация, как у DroppedStreamMessages)
Task<ConnectionWaitOutcome> WaitForConnectionOutcomeAsync(
    TimeSpan? timeout = null,
    CancellationToken cancellationToken = default);
```

**Область действия вместо инварианта.** Первая редакция объявляла соответствие 1:1 между
перечислением и типами из 2.1. Это было ложно в обе стороны: `TimedOut` соответствует
`System.TimeoutException`, который раздел 7 намеренно оставляет типом BCL, а
`RequestRefusedException` и `ConnectionSupersededException` относятся к допуску запроса и
владению переходом, а не к ожиданию готовности, и парного значения не имеют.

Соответствие определено **только для того, что бросает `WaitForConnectionAsync`**, и полностью:

| Исключение | Точка | Значение |
|---|---|---|
| `ClientDisconnectedException` | `:1392`, и `:2530` через `:1368` | `Disconnected` |
| `ConnectHandlerFailedException` | те же точки при причине `ConnectHandlerGaveUp` | `ConnectHandlerFailed` |
| `ReconnectExhaustedException` | `:1399` | `ReconnectExhausted` |
| `NotConnectingException` | `:2542` через `:1368` | `NotConnecting` |
| `System.TimeoutException` | `:1406` | `TimedOut` |
| `OperationCanceledException` (токен вызывающего) | `:1412`, `:1421` | **пробрасывается** |
| `ArgumentOutOfRangeException` (некорректный таймаут) | `:1374` | **пробрасывается** |

Отмена собственным токеном и некорректный аргумент остаются исключениями: первое — конвенция
.NET, второе — ошибка вызывающего, а не результат работы соединения.

**`HasConnectionAsync` не трогаем и на интерфейс не поднимаем.** Перегрузка
`HasConnectionAsync(TimeSpan? = null, CancellationToken = default)` рядом с существующей
`HasConnectionAsync(TimeSpan? = null)` делает вызов без аргументов неоднозначным: обе
перегрузки применимы, обеим нужна подстановка умолчаний, правило «лучшего члена» не выбирает —
CS0121. В репозитории вызовов без аргументов нет, поэтому сборка бы прошла, а сломались бы
потребители — при заявленной аддитивности. А переопределение её через
`outcome == Connected` изменило бы наблюдаемое поведение: сегодня она пропускает
`NotConnectedException` наружу, и существующие `catch` у потребителей перестали бы срабатывать.
Метод остаётся как есть, на `Connection`; на интерфейс идёт один член на одно понятие —
`WaitForConnectionOutcomeAsync`.

**Default-реализация и класс.** Default-член интерфейса вызывается только через ссылку типа
`IXrplClient`; через ссылку типа `XrplClient` он не виден. Поэтому метод добавляется и в класс —
так же, как сделано с `DroppedStreamMessages` (`IXrplClient.cs:744`). Обе формы вызова
закрепляются тестами.

### 3.2. Событие вместо опроса

**Реализовано; описание ниже — того, что построено, а не того, что планировалось.** Первая
редакция этого раздела предлагала переиспользовать `ConnectionManager` — примитив ожидания,
который в SDK уже есть, уже разослан по жизненному циклу и у которого не хватает только
слушателя (`AwaitConnection()` не вызывается нигде). От этого пришлось отказаться при
реализации, и причина важнее самой правки.

**Почему `ConnectionManager` не подходит.** Его `RejectAllAwaiting*` вызывается в том числе на
шести точках **отставки** соединения. Но отставка — не повод прекращать ждать: клиент
пересобирает соединение, и запрос под `RequestFailurePolicy.WaitForConnection` обязан
перенестись на новое соединение. Это записанный контракт (changelog 11.3.2.0: «`WaitForConnection`
carries it over to the new connection»), и разбудив ожидающего на отставке, мы превратили бы
перенос в отказ. Сегодня это незаметно ровно потому, что `AwaitConnection` никто не слушает.

**Что построено вместо.** Отдельный сигнал готовности на `TaskCompletionSource<bool>`:

- завершается значением, а не исключением. Ждать может никто, а завершённая исключением задача
  без наблюдателя — это unobserved exception; причину по-прежнему строит точка броска;
- `RunContinuationsAsynchronously` обязателен: сигнал завершается изнутри критических секций,
  двигающих соединение, и без него каждый ожидающий возобновлялся бы прямо там — дефект того же
  класса, что #177;
- **пробуждение всегда перевзводит сигнал** — в той же критической секции, что и завершение.
  Это не украшение, а исправление дефекта, который поймал тест
  `TestUSwitchingServersSurvivesAConnectionThatSettlesOnRetry`: соединение может уйти через
  close-callback сокета, который продолжает поколение и не делает takeover. Взводя сигнал только
  на takeover, мы оставляли его завершённым после такого закрытия, и ожидающий крутился вхолостую
  до собственного таймаута;
- будит единственная воронка состояний — `SetConnectionState`. Через неё проходит каждое
  состояние соединения, поэтому забыть терминальное условие структурно невозможно; отдельная
  правка «разбудить на исчерпании цикла», которую предписывала первая редакция, не понадобилась.
  Плюс явное пробуждение в `OnceOpen` **до** `OnConnected`: ожидающий ждёт сокет, а не
  потребительский обработчик.

**`ConnectionManager` всё равно чинится — отдельно от сигнала.** Отказавшись строить ожидание
на нём, оставить его как есть нельзя: `connectionManager` — публичное поле публичного типа
(`connection.cs:1323`), достижимое снаружи как
`client.connection.connectionManager.AwaitConnection()`, и соединение шлёт в него уведомления с
девяти точек, на своих потоках. Внутри SDK его никто не ждёт, поэтому дефекты не проявлялись —
но они не отсутствуют, а спят, и первый же потребитель, который вызовет `AwaitConnection()`,
встретит их все сразу. Проверено тестами до правки: ожидающий возобновлялся **внутри**
`ResolveAllAwaiting`, то есть внутри `OnceOpen` до `OnConnected`, а регистрация, пришедшаяся на
рассылку, роняла `InvalidOperationException` («Collection was modified») или теряла ожидающего
насовсем. Исправлено: список под блокировкой, снимок берётся под ней, а будятся ожидающие вне
её; `RunContinuationsAsynchronously`; `TrySet*` вместо `Set*`; отмена через `TrySetCanceled`.

**Что осталось в `WaitForConnectionAsync` без изменений.** Все проверки: `IsConnected()`,
`CheckIfNotConnected()`, исчерпание бюджета, таймаут, токен. Сигнал говорит только «посмотри
ещё раз», решение принимают те же условия, что и раньше — поэтому наблюдаемое поведение
идентично опросу. Ушёл ровно `Task.Delay(100 ms)`, на его месте
`ready.WaitAsync(remaining, cancellationToken)`.

**Результат.** Весь юнит-набор (1304 теста) зелёный в трёх прогонах подряд, и он же стал
быстрее: 43 секунды до правки, 30 после — на тестах соединения опрос был заметной долей времени.

## 4. Решение, часть 3: терминальное уведомление называет причину

Первая редакция добавляла `bool Exhausted` в `ReconnectInfo` и заполняла его на терминальном
уведомлении цикла. Это давало **два** правила чтения вместо одного: `Reconnect != null`
переставало значить «цикл работает», и различать приходилось по новому флагу. Признак стоит не
на том объекте.

Вместо этого причина останова ставится на уведомление целиком:

```csharp
public enum ConnectionStopReason
{
    None,                    // уведомление не терминальное
    UserDisconnected,
    ReconnectExhausted,
    ConnectHandlerFailed,
    InitialConnectionFailed,
    ClosedPermanently,       // соединение закрыто без переподключения
}

public class ConnectionStatusInfo
{
    // ...существующие члены без изменений...
    public ConnectionStopReason StopReason { get; set; }   // умолчание None — аддитивно
}
```

Заполняется на всех десяти точках `Disconnected`:

| Точки | `StopReason` |
|---|---|
| `:1792`, `:1803`, `:1856`, `:1866` | `UserDisconnected` |
| `:3202` | `ReconnectExhausted` |
| `:2774` | `ConnectHandlerFailed` |
| `:2350` | `InitialConnectionFailed` |
| `:2291`, `:3041`, `:3072` | `ClosedPermanently` |

`ReconnectInfo` не меняется вовсе, `Reconnect` на терминальном уведомлении остаётся `null`, и
семантика `Reconnect != null` сохраняется. Этим же закрывается бывший открытый вопрос об отказе
на обработчике: на пути статуса он теперь называется, а не выводится из текста.

## 5. Тесты

**Написано 37, все зелёные.** Ниже — что именно они закрепляют; список отражает реализацию, а не
первоначальный замысел.

`Tests/Xrpl.Tests/Client/Exceptions/TestUConnectionOutcomeTypes.cs` — 9 тестов о самих типах:
конструктор с причиной; все пять новых типов остаются `NotConnectedException`; `Attempts` равен
бюджету, а не сырому счётчику; `Failures` и `InnerException` у отказа обработчика; перекрытие
несёт вид и адрес; токена не несёт; подтип переживает прямой `await`; переживает `Task.WhenAll`
без сбойных задач; теряется рядом со сбойной.

`Tests/Xrpl.Tests/Client/TestUConnectionManagerWaiters.cs` — 3 теста о примитиве ожидания:
ожидающий не возобновляется внутри уведомления; отмена даёт `Canceled`; регистрация во время
рассылки никого не теряет.

`Tests/Xrpl.Tests/Client/TestUConnectionOutcomes.cs` — 25 тестов о том, какой путь даёт какой
ответ:

| Что закрепляется | Тест |
|---|---|
| Подключения нет вовсе | `AClientThatNeverConnectedReportsThatNothingIsInProgress` |
| Бюджет переподключения исчерпан | `AWaiterLearnsTheReconnectBudgetWasSpent` |
| Пробуждение по факту отказа, а не по таймауту | `AWaiterIsWokenWhenTheClientGivesUpNotWhenItsOwnTimeoutExpires` |
| Отказ обработчика ≠ отключение потребителем | `GivingUpOnABrokenConnectHandlerSaysTheHandlerBroke` |
| Тот же ответ при обоих таймингах отказа | `ABrokenConnectHandlerAnswersTheSameWhicheverWayItFails` |
| Перекрытие другим `ChangeServer` | `AnOvertakenSwitchNamesTheSwitchThatWon` |
| То же на уровне `XrplClient` | `AnOvertakenSwitchIsReportedThroughTheClientToo` |
| Перекрытие **во время ожидания** (путь `:1153`) | `ASwitchOvertakenWhileWaitingReportsTheWinner` |
| Перекрытие `Connect` | `ASwitchAConnectOvertookNamesTheConnect` |
| Перекрытие `Disconnect` даёт «клиент выключен» | `ASwitchADisconnectOvertookIsToldTheClientIsDown` |
| Отказ запроса по `ImmediateFail` | `ARequestRefusedByPolicySaysSoRatherThanBlamingTheServer` |
| Свип запроса переключением | `ARequestSweptByASwitchNamesTheSwitch` |
| Свип запроса отключением | `ARequestSweptByADisconnectNamesTheDisconnect` |
| Свип запроса реконнектом | `ARequestSweptByAReconnectNamesTheReconnect` |
| Сетевой обрыв перехода не называет | `ARequestKilledByANetworkDropNamesNoTransition` |
| Повторный `Connect` запросы не трогает | `RedundantConnectDoesNotDisturbRequestsInFlight` |
| `StopReason` при исчерпании, `Reconnect` остаётся `null` | `TheTerminalNotificationSaysWhyTheClientStopped` |
| `StopReason` при отключении потребителем | `AConsumerDisconnectIsNamedInTheStatusStream` |
| `StopReason` при отказе обработчика и `None` на нетерминальных | `TheStatusStreamNamesABrokenHandlerAndOnlyWhenTerminal` |
| Исход как значение | `TheOutcomeOfAWaitIsAValueAndNamesTheCase` |
| Доступность через интерфейс и через класс | `TheOutcomeIsReachableThroughTheInterfaceAndTheClass` |
| Таймаут — ответ, отмена и плохой таймаут — исключения | `TimeoutIsAnAnswerAndCancellationIsNot` |
| Ожидающие не роняют друг друга | `WaitersDoNotTakeEachOtherDown` |
| `ChangeServer` переживает пересборку соединения | `SwitchingServersSurvivesAConnectionThatSettlesOnRetry` |
| Исчерпав бюджет, клиент остаётся остановленным — и не залипает | `AClientThatSpentItsReconnectBudgetStaysStopped` |

Вспомогательные серверы, которых не хватало: `DropsFirstServerInfoServer` (роняет первый
`server_info` — единственное окно, где видно разницу в 2.7) и `SilentOnPingAndLedgerServer`
(молчит на `ping` и на запрос — позволяет держать запрос в полёте, пока health-check
пересобирает соединение).

Отдельного теста «совместимость `catch`» нет: `Assert.IsInstanceOfType<T>` и
`Helper.ThrowsExceptionAsync<T>` принимают наследников, а существующие тесты
(`TestUConnectionTransitionOwner`, `TestUOnConnectedHandlerFailure`,
`TestURequestDuringServerSwitch`) уже ловят базовыми типами и потому сами и есть эта проверка —
ни один из них не потребовал правок.

**Про гонку из 2.4.** Сценарий, который ей нужен, воспроизводил другой дефект — и тот теперь
исправлен здесь же (раздел 10), а «ровно одна серия» стала утверждением
`AClientThatSpentItsReconnectBudgetStaysStopped`. Отдельного теста на само сужение проверки
`:1339` нет: он требует попасть в зазор между отказом цикла и входом в ожидание, чего публичный
API детерминированно не даёт. Обоснование, почему сужать нельзя, остаётся в 2.4 и опирается на
достижимость `NotConnectingException` из `CheckIfNotConnected`.

## 6. Расхождения с предложением

Принято по существу. Отличия:

1. **Типов не два, а пять** (раздел 1.1). `RequestRefusedException`,
   `ConnectHandlerFailedException` и `NotConnectingException` в предложении отсутствуют, но
   смешивают случаи, реакция на которые различна вплоть до противоположной: «узел не отвечает,
   уходим» против «узел отвечает, отказал наш обработчик» против «вызови `Connect()`».
2. **`ConnectionSupersededException` несёт `Kind`, а не только `SupersededBy`**, и `Kind`
   читается из состояния перехода, а не подставляется по месту.
3. **`bool` заменён перечислением** (раздел 3.1) — по той же причине, по которой предложение
   отвергает таблицу соответствий. Предложенная форма `Task<bool>` не различала бы «сдался» и
   «не успел».
4. **`WaitForConnectionAsync` потребителю формально доступен** — через
   `IXrplClient.connection` (`IXrplClient.cs:85`), вопреки пункту 2 предложения. Проблема не в
   недоступности, а в том, что путь идёт через внутренний объект. На `XrplClient` метода нет
   вовсе — предложение считает, что есть.
5. **`HasConnectionAsync` не поднимается на интерфейс** (раздел 3.1), хотя предложение просило
   именно `bool`-форму: перегрузка ломающая, а переопределение меняет поведение.
6. **Событийное ожидание сделано не донорским классом, а починкой своего** (раздел 3.2):
   примитив в SDK уже был и уже был разослан по жизненному циклу — не хватало слушателя и
   одного пробуждения.
7. **Причина ставится на `ConnectionStatusInfo`, а не на `ReconnectInfo`** (раздел 4) — сильнее
   того, что просило предложение, и не ломает смысл `Reconnect != null`.

Пункт 1 предложения (`Xrpl.Client.Exceptions.TimeoutException` на пути `ChangeServer`)
подтверждается полностью — см. 1.5.

## 7. Что не меняется, и где граница

- Поведение соединения. Ни один переход, ни один порядок, ни одно уведомление не меняет момента
  или условия. Меняется только то, что можно прочитать о результате.
- Внутренняя проверка на `connection.cs:1339` остаётся широкой (2.4).
- `HasConnectionAsync` остаётся ровно такой, какая есть (3.1).
- Тексты сообщений исключений — дословно те же.
- `System.TimeoutException` на таймауте ожидания (`connection.cs:1406`) остаётся типом BCL:
  это правильный тип, и подменять его своим значило бы добавить ловушку, а не убрать.
- `Xrpl.Client.Exceptions.TimeoutException` не трогаем. Ловушка «это не
  `System.TimeoutException`» остаётся; она уже описана в XML-документации типа, а
  переименование ломающее.
- **Свип, вызванный отказом соединения, остаётся отменой.** Сетевой обрыв и закрытие сокета
  (`:2290`/`:2291`, `:2310`/`:2311`, `:2995`) отклоняют запросы через
  `RejectAllWithCancellation`. Это решение, а не упущение: комментарий на `:2992` фиксирует
  его — отмена выбрана, чтобы приложения-потребители не писали сетевой обрыв в Critical.
  Причину такой остановки даёт `StopReason` на потоке статуса (раздел 4). Свипы, которые делает
  переход, типизируются (2.6).
- `:2315`, `:2516`, `:2522` не трогаются: первый — мёртвый путь, второй и третий — остаточные
  ветки (1.1).
- Базовые пакеты (`Xrpl.AddressCodec`, `Xrpl.BinaryCodec`, `Xrpl.Keypairs`) не затронуты и
  версию не получают.

## 8. Решения по бывшим открытым вопросам

Открытых вопросов не осталось; ниже — что решено и почему, чтобы к этому не возвращались.

1. **Свипы запросов в полёте — типизируются** (2.6). Все шесть точек, где свип делает переход,
   отклоняют запросы `ConnectionSupersededException` с видом перехода. Тип наследует
   `OperationCanceledException`, поэтому существующие `catch` и статусы задач не меняются, и
   отдельного релиза это не требует. Свипы, вызванные отказом соединения, остаются отменой
   намеренно (раздел 7).
2. **Готовый примитив ожидания со стороны потребителей не берётся** (3.2). Правовых
   препятствий к этому не было, но код и не нужен: `ConnectionManager` уже реализует этот
   примитив и уже разослан по всем точкам жизненного цикла, кроме одной. Берём своё, чиним его
   дефекты и добавляем недостающее пробуждение на `:3202`. В ответе на запрос объяснить, что
   вместо вставки нового класса чинится тот, который в SDK лежал без дела.
3. **`SetNetworkId` на пути `ChangeServer` — защищается повторами здесь же** (2.7), вместе с
   уточнением фильтра повторов под новые типы. Отдельной задачи не заводим: правка на две
   строки, а её взаимодействие с `ConnectionSupersededException` всё равно проектируется в этой
   же спеке, и разносить их по разным релизам значило бы проектировать дважды.

## 9. Версия и changelog

Аддитивно, с двумя оговорками, которые записаны честно, а не спрятаны:

- новые типы наследуют бросаемым сегодня, поэтому ни один `catch` по базовому типу не меняет
  смысла. Проверки на **точный** тип, фильтры `catch when`, сериализация исключений и
  утверждения `ThrowsExactly` в чужих тестах — меняются. Это уточнение наследованием, а не
  «всё аддитивно»;
- новые члены интерфейса имеют default-реализацию (главная библиотека таргетит
  `net8.0;net9.0;net10.0`), но default-член виден только через ссылку типа интерфейса, поэтому
  метод добавляется и в `XrplClient` (3.1).

`ConnectionStopReason.None` — умолчание, `ReconnectInfo` не меняется, `HasConnectionAsync` не
меняется.

**Внутренняя механика меняется в двух местах**, и оба наблюдаемого поведения не затрагивают:

- свипы запросов отдают наследника `OperationCanceledException` вместо него самого (2.6) — ни
  один `catch`, ни один статус задачи, ни один тест не меняется;
- ожидание готовности перестаёт опрашивать и начинает слушать `ConnectionManager` (3.2). Здесь
  же добавляется пробуждение на исчерпании цикла (`:3202`), которого не было. Это единственное
  место, где поведение строго улучшается: ожидающий, который раньше досиживал до
  `ConnectionAcquisitionTimeout`, теперь узнаёт причину сразу. Закреплено тестом 16.

Минорная версия: **11.5.0.0**, `<PackageVersion>` только в `Xrpl/Xrpl.csproj`. Один PR, один
релиз, возврата к этому коду не планируется.

Запись в `CHANGES.md` — по образцу 11.4.0.0: какие типы читать вместо классификации по тексту,
что благодаря этому потребитель может удалить у себя (написанные вручную таблицы соответствий
«тип исключения → что случилось с соединением» и внешние флаги вида «это отключение моё»), и
где сознательная граница — свип, вызванный сетевым обрывом, остаётся отменой, а причину даёт
`StopReason`.

## 10. `StopAfterMaxAttempts` действительно останавливает клиента

Найдено при попытке написать тест 9 и сначала вынесено из объёма как посторонний дефект; по
решению пользователя разобрано и исправлено здесь же — это тот же жизненный цикл соединения,
что и вся работа.

**Симптом.** Клиент подключён, пир закрывает сокет (код 1001), цикл переподключения тратит
бюджет и объявляет `Disconnected` / `ReconnectExhausted` — а следом стартует **вторая полная
серия** с попытки №1 и объявляет то же самое второй раз.

**Не регрессия этой работы.** Проверено прогоном на неизменённом 11.4.0 (`f7040872`) в
отдельном worktree: последовательность уведомлений идентична. От health-check не зависит —
воспроизводится при `UseCustomPing = false`.

**Корневая причина.** `StartReconnectLoop` отличает «серия уже идёт» по двум полям:
`_reconnectLoopGeneration` и `_reconnectCts`. Выходная бухгалтерия цикла обнуляет **оба** — то
есть оставляет ровно ту картину, которая означает «цикла нет». Закрытие сокета от последней
неудачной попытки приходит уже после этого, проходит проверку, а поскольку источник отмены
обнулён — попадает в ветку «свежая последовательность» и ставит `_reconnectAttempts` в ноль.
Факта «это поколение сдалось» не записывал никто: `_permanentlyDisconnected` ставит только
`Disconnect()`, а исчерпание бюджета — нет.

**Исправление.** Поколение, чья серия исчерпала бюджет, запоминается
(`_reconnectExhaustedGeneration`) в ветке отказа, под `_transitionLock` и **до** уведомления —
по той же причине, по которой цикл запускают до уведомления: уведомление исполняет
потребительский код, и закрытие может лечь прямо в него. `StartReconnectLoop` отказывается
стартовать для такого поколения.

Ключ — поколение, а не флаг, поэтому сбрасывать нечего: номера поколений только растут, и
`Connect()` или `ChangeServer` начинают новое — ровно тогда, когда спросить снова решил
потребитель, и это разрешено.

**Закреплено** `TestUAClientThatSpentItsReconnectBudgetStaysStopped`: ровно одно терминальное
уведомление, и вторым утверждением — что остановленный клиент не залип: `ChangeServer` на живой
сервер подключается. Без правки тест падает на «Actual: 2».

**Замечание о транспорте.** В браузере дефект не воспроизводился: там неудачная попытка кончается
`net_webstatus_ConnectFailure` без закрытия сокета, поэтому второго входа в close-callback нет.
Исправление от транспорта не зависит — оно на стороне решения о запуске серии.
