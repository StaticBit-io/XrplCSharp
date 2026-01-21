# Руководство по подключению XrplCSharp

Это руководство объясняет, как настроить и управлять WebSocket-подключениями к узлам XRP Ledger с помощью библиотеки XrplCSharp.

## Содержание

- [Быстрый старт](#быстрый-старт)
- [Параметры подключения](#параметры-подключения)
- [Состояния подключения](#состояния-подключения)
- [Автоматическое переподключение](#автоматическое-переподключение)
- [Политики обработки запросов](#политики-обработки-запросов)
- [Обработка событий](#обработка-событий)
- [Обработка ошибок](#обработка-ошибок)
- [Примеры использования](#примеры-использования)

---

## Быстрый старт

```csharp
using Xrpl.Client;

// Создание клиента с настройками по умолчанию
var client = new XrplClient("wss://s.altnet.rippletest.net:51233");

// Подключение
await client.Connect();

// Выполнение запросов
var response = await client.Request(new AccountInfoRequest { Account = "rAddress..." });

// Отключение по завершении
await client.Disconnect();
```

---

## Параметры подключения

Настройте поведение подключения, передав `ClientOptions` при создании клиента:

```csharp
var client = new XrplClient("wss://s.altnet.rippletest.net:51233", new XrplClient.ClientOptions
{
    RequestTimeout = TimeSpan.FromSeconds(30),
    MaxReconnectAttempts = 10,
    StopAfterMaxAttempts = false
});
```

### Доступные параметры

| Параметр | Тип | По умолчанию | Описание |
|----------|-----|--------------|----------|
| `RequestTimeout` | TimeSpan | 20 секунд | Таймаут для отдельных API-запросов после установки соединения |
| `ConnectionAttemptTimeout` | TimeSpan | 20 секунд | Таймаут для одной попытки подключения WebSocket |
| `ReconnectBaseDelay` | TimeSpan | 2 секунды | Базовая задержка между попытками автоматического переподключения |
| `ReconnectMaxDelay` | TimeSpan | 30 секунд | Максимальная задержка между попытками переподключения (предел экспоненциального роста) |
| `MaxReconnectAttempts` | int | 5 | Максимальное количество попыток переподключения после разрыва соединения |
| `StopAfterMaxAttempts` | bool | true | Прекращать ли попытки переподключения после достижения максимума |
| `UseCustomPing` | bool | true | Включить пользовательский ping/pong для обнаружения проблем соединения |
| `RequestPolicy` | RequestFailurePolicy | WaitForConnection | Как обрабатывать запросы при отсутствии соединения |
| `ConnectionAcquisitionTimeout` | TimeSpan | 5 минут | Максимальное время ожидания соединения при использовании политики WaitForConnection |

---

## Состояния подключения

Соединение может находиться в одном из четырёх состояний, доступных через `client.connection.CurrentConnectionState`:

| Состояние | Описание |
|-----------|----------|
| `Disconnected` | Не подключён. Начальное состояние, после отключения пользователем или после превышения максимального числа попыток |
| `Connecting` | Установка начального соединения |
| `Connected` | Успешно подключён и готов к запросам |
| `RestoringConnection` | Попытка восстановить соединение после неожиданного разрыва |

### Диаграмма состояний

```
                    ┌─────────────────┐
                    │  Disconnected   │ (начальное)
                    └────────┬────────┘
                             │ Connect()
                             ▼
                    ┌─────────────────┐
                    │   Connecting    │
                    └────────┬────────┘
                             │ успех
                             ▼
                    ┌─────────────────┐
         ┌─────────│    Connected    │◄────────┐
         │         └────────┬────────┘         │
         │                  │ потеря связи     │ успех
         │                  ▼                  │
         │         ┌─────────────────┐         │
         │         │RestoringConnection│───────┘
         │         └────────┬────────┘
         │                  │ макс. попыток или Disconnect()
         │                  ▼
         │         ┌─────────────────┐
         └────────►│  Disconnected   │
   пользователь   └────────────────┘
   вызвал Disconnect()
```

---

## Автоматическое переподключение

При неожиданной потере соединения (перезапуск сервера, проблемы сети) клиент автоматически пытается переподключиться.

### Алгоритм задержки

Задержка между попытками переподключения использует экспоненциальный рост с джиттером:

```
задержка = min(ReconnectBaseDelay * 2^(попытка-1), ReconnectMaxDelay) + случайный_джиттер
```

Пример с настройками по умолчанию:
- Попытка 1: ~2 секунды
- Попытка 2: ~4 секунды
- Попытка 3: ~8 секунд
- Попытка 4: ~16 секунд
- Попытка 5: ~30 секунд (достигнут предел)

### Поведение переподключения

| Сценарий | Поведение |
|----------|-----------|
| Сервер закрыл соединение | Автопереподключение начинается |
| Обнаружен таймаут сети | Автопереподключение начинается |
| Пользователь вызвал `Disconnect()` | Автопереподключения нет, состояние становится `Disconnected` |
| Превышено макс. попыток (`StopAfterMaxAttempts = true`) | Переподключение останавливается, состояние `Disconnected` |
| Превышено макс. попыток (`StopAfterMaxAttempts = false`) | Продолжает попытки с предупреждающими сообщениями |

### Ручное переподключение

После исчерпания максимального числа попыток (с `StopAfterMaxAttempts = true`) вы можете переподключиться вручную:

```csharp
// После окончательного сбоя соединения
await client.Connect();
```

Это сбрасывает счётчик попыток и начинает заново.

### Быстрое переподключение (Fast Reconnect)

Для определённых сценариев библиотека использует **быстрое переподключение** (3-5 секунд) вместо экспоненциальной задержки:

| Сценарий | Поведение | Время |
|----------|-----------|-------|
| Вызван `ChangeServer()` | Немедленное переключение на новый сервер | 3-5 секунд |
| Таймаут ping (нет pong 15 сек) | Немедленное переподключение к тому же серверу | 3-5 секунд |
| Потеря сети (IOException, SocketException) | Немедленное переподключение при восстановлении сети | 3-5 секунд |

Быстрое переподключение отличается от стандартного:
- **Без экспоненциальной задержки** - подключается немедленно
- **Изоляция сессий** - старая сессия завершается, создаётся новая
- **Отмена pending-запросов** - избегает устаревших ответов от старого соединения
- **ReconnectInfo доступен** - `CurrentAttempt = 1` во время быстрого переподключения

```csharp
client.connection.OnConnectionStatus += (status) =>
{
    if (status.ConnectionState == XrpConnectionState.RestoringConnection)
    {
        // ReconnectInfo всегда доступен во время переподключения (включая быстрое)
        Console.WriteLine($"Переподключение: попытка {status.Reconnect?.CurrentAttempt}");
    }
};
```

---

## Особенности MAUI и мобильных приложений

При использовании XrplCSharp в MAUI или мобильных приложениях библиотека обеспечивает специальную обработку мобильных сетевых условий.

### Отсутствие Critical-логирования

Библиотека подавляет Critical-уровень логирования для типичных мобильных сетевых исключений:
- `ObjectDisposedException` - сокет закрыт во время переподключения
- `IOException` - ошибки сетевого ввода-вывода
- `SocketException` - низкоуровневые ошибки сокета
- `TaskCanceledException` - операции отменены при отключении
- Ошибки DNS на iOS (например, "nodename nor servname provided")

Это предотвращает переполнение глобальных обработчиков исключений вашего приложения ожидаемыми сетевыми событиями.

### Автоматическое восстановление сети

При восстановлении сетевого подключения (например, переключение с WiFi на мобильную сеть) библиотека:
1. Обнаруживает потерю сети через таймаут ping или исключение сокета
2. Инициирует быстрое переподключение (не медленный exponential backoff)
3. Переподключается в течение 3-5 секунд
4. Отправляет обновления статуса `RestoringConnection` → `Connected`

### Специфика iOS

Библиотека распознаёт специфические для iOS сетевые ошибки:
- Ошибки разрешения DNS с HRESULT `0xFFFDFFFF`
- Общие ошибки с HRESULT `0x80004005` (E_FAIL)
- Паттерны сообщений об ошибках типа "nodename nor servname"

Они обрабатываются как восстановимые потери сети, а не как критические ошибки.

### Лучшие практики для мобильных приложений

```csharp
var client = new XrplClient(url, new XrplClient.ClientOptions
{
    // Мобильные сети ненадёжны - будьте терпеливы
    MaxReconnectAttempts = 50,
    StopAfterMaxAttempts = false,
    
    // Ждать соединения - мобильное устройство может временно потерять связь
    RequestPolicy = RequestFailurePolicy.WaitForConnection,
    ConnectionAcquisitionTimeout = TimeSpan.FromMinutes(5),
    
    // Держите ping включённым для проактивного обнаружения сбоев
    UseCustomPing = true
});

// Мониторинг соединения для обновления UI
client.connection.OnConnectionStatus += (status) =>
{
    MainThread.BeginInvokeOnMainThread(() =>
    {
        UpdateConnectionIndicator(status.ConnectionState);
    });
};
```

---

## Политики обработки запросов

Параметр `RequestPolicy` определяет, как обрабатываются запросы при отсутствии соединения.

### ImmediateFail

Запросы немедленно выбрасывают `NotConnectedException`, если нет соединения:

```csharp
var client = new XrplClient(url, new XrplClient.ClientOptions
{
    RequestPolicy = RequestFailurePolicy.ImmediateFail
});

try
{
    var response = await client.Request(...);
}
catch (NotConnectedException)
{
    // Обработка отсутствия соединения
}
```

**Когда использовать:** Когда нужна немедленная обратная связь и вы сами управляете повторами.

### WaitForConnection (По умолчанию)

Запросы ожидают установки соединения (до `ConnectionAcquisitionTimeout`):

```csharp
var client = new XrplClient(url, new XrplClient.ClientOptions
{
    RequestPolicy = RequestFailurePolicy.WaitForConnection,
    ConnectionAcquisitionTimeout = TimeSpan.FromMinutes(2)
});

// Это будет ждать соединения, если отключено
var response = await client.Request(...);
```

**Когда использовать:** Для ботов и сервисов 24/7, которые должны переживать временные проблемы сети.

---

## Обработка событий

### События статуса подключения

Подпишитесь на `OnConnectionStatus` для получения обновлений состояния в реальном времени:

```csharp
client.connection.OnConnectionStatus += (status) =>
{
    Console.WriteLine($"Состояние: {status.ConnectionState}");
    Console.WriteLine($"Сообщение: {status.Message}");
    Console.WriteLine($"Важность: {status.Severity}");
    
    if (status.Reconnect != null)
    {
        Console.WriteLine($"Попытка: {status.Reconnect.CurrentAttempt}/{status.Reconnect.MaxAttempts}");
        Console.WriteLine($"Следующая попытка через: {status.Reconnect.RemainingDelay.TotalSeconds}с");
    }
};
```

### Свойства ConnectionStatusInfo

| Свойство | Тип | Описание |
|----------|-----|----------|
| `ConnectionState` | XrpConnectionState | Текущее состояние подключения |
| `Message` | string | Человекочитаемое сообщение о статусе |
| `Severity` | ConnectionCloseSeverity | Info, Warning или Error |
| `Reconnect` | ReconnectInfo? | Детали переподключения (null если не переподключается) |

### Свойства ReconnectInfo

| Свойство | Тип | Описание |
|----------|-----|----------|
| `CurrentAttempt` | int | Номер текущей попытки переподключения |
| `MaxAttempts` | int | Максимальное настроенное количество попыток |
| `RemainingDelay` | TimeSpan | Время до следующей попытки переподключения |

### Другие события

```csharp
// Вызывается при установке соединения
client.connection.OnConnected += () => { ... };

// Вызывается при потере соединения
client.connection.OnDisconnect += (code, reason) => { ... };

// Вызывается при ошибках
client.connection.OnError += (errorCode, errorMessage, message, error) => { ... };
```

---

## Обработка ошибок

### Распространённые исключения

| Исключение | Когда возникает |
|------------|-----------------|
| `NotConnectedException` | Запрос при отсутствии соединения (с политикой ImmediateFail) |
| `TimeoutException` | Превышен таймаут запроса (`RequestTimeout`) |
| `DisconnectedException` | Соединение потеряно во время ожидающего запроса |
| `XrplException` | Ошибки протокола XRPL |

### Обработка отключений

```csharp
client.connection.OnConnectionStatus += (status) =>
{
    switch (status.ConnectionState)
    {
        case XrpConnectionState.Disconnected:
            if (status.Severity == ConnectionCloseSeverity.Error)
            {
                // Постоянный сбой - может потребоваться ручное вмешательство
                LogError(status.Message);
            }
            break;
            
        case XrpConnectionState.RestoringConnection:
            // Автопереподключение в процессе
            LogInfo($"Переподключение... попытка {status.Reconnect?.CurrentAttempt}");
            break;
    }
};
```

---

## Примеры использования

### Базовое подключение с мониторингом статуса

```csharp
var client = new XrplClient("wss://s.altnet.rippletest.net:51233");

client.connection.OnConnectionStatus += (status) =>
{
    Console.WriteLine($"[{status.ConnectionState}] {status.Message}");
};

await client.Connect();
// ... использование клиента ...
await client.Disconnect();
```

### Конфигурация для бота 24/7

```csharp
var client = new XrplClient("wss://s.altnet.rippletest.net:51233", new XrplClient.ClientOptions
{
    // Никогда не прекращать попытки переподключения
    MaxReconnectAttempts = 100,
    StopAfterMaxAttempts = false,
    
    // Ждать соединения при запросах
    RequestPolicy = RequestFailurePolicy.WaitForConnection,
    ConnectionAcquisitionTimeout = TimeSpan.FromMinutes(10),
    
    // Увеличенные таймауты для стабильности
    RequestTimeout = TimeSpan.FromSeconds(60),
    ConnectionAttemptTimeout = TimeSpan.FromSeconds(30)
});
```

### Конфигурация с быстрым отказом

```csharp
var client = new XrplClient("wss://s.altnet.rippletest.net:51233", new XrplClient.ClientOptions
{
    // Немедленный отказ, если нет соединения
    RequestPolicy = RequestFailurePolicy.ImmediateFail,
    
    // Ограниченное количество попыток
    MaxReconnectAttempts = 3,
    StopAfterMaxAttempts = true,
    
    // Короткие таймауты
    RequestTimeout = TimeSpan.FromSeconds(10),
    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5)
});
```

### Переключение серверов

```csharp
// Переключение на другой сервер (отключается и переподключается)
await client.connection.ChangeServer("wss://s1.ripple.com:443");
```

---

## Лучшие практики

1. **Всегда подписывайтесь на `OnConnectionStatus`** для мониторинга состояния соединения
2. **Используйте политику `WaitForConnection`** для сервисов, требующих отказоустойчивости
3. **Установите `StopAfterMaxAttempts = false`** для приложений 24/7
4. **Обрабатывайте состояние `Disconnected`** в вашем UI для отображения статуса подключения
5. **Не вызывайте `Connect()` повторно** - это безопасно, но излишне (идемпотентно)
6. **Используйте `CancellationToken`** для корректного завершения работы
