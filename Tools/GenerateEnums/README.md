# GenerateEnums

Генератор C# исходных файлов из `definitions.json` для проекта `Xrpl.BinaryCodec`.

## Что генерируется

| Файл | Источник в JSON | Содержимое |
|------|----------------|------------|
| `EngineResult.Generated.cs` | `TRANSACTION_RESULTS` | Коды результатов транзакций |
| `TransactionType.Generated.cs` | `TRANSACTION_TYPES` | Типы транзакций |
| `LedgerEntryType.Generated.cs` | `LEDGER_ENTRY_TYPES` | Типы объектов леджера |
| `Field.{Type}.Generated.cs` | `FIELDS` | Поля, сгруппированные по типу данных |

Все файлы создаются в `Base/Xrpl.BinaryCodec/Enums/`.

## Запуск

```bash
# Из корня репозитория
dotnet run --project Tools/GenerateEnums

# С указанием пути к definitions.json
dotnet run --project Tools/GenerateEnums -- path/to/definitions.json

# Принудительная перезапись всех файлов (даже без изменений)
dotnet run --project Tools/GenerateEnums -- --force
```

## Семантическое сравнение

Перед записью генератор сравнивает данные из `definitions.json` с содержимым существующих `.Generated.cs` файлов:

- **Парсит** уже сгенерированные файлы (имена, коды, параметры полей)
- **Сравнивает** с новыми данными из JSON
- **Выводит** отчёт о добавленных (+), удалённых (-) и изменённых (~) записях
- **Пропускает** запись файла если изменений нет

Пример вывода при наличии изменений:

```
[TransactionType] 78 entries:
    + NewTxType (code: 99)
    - OldTxType (was code: 42)
    ~ Payment: code 0 -> 1
  -> Written: TransactionType.Generated.cs (78 entries)
```

## Обновление definitions.json

1. Скачать актуальный `definitions.json` из [xrpl.js](https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-binary-codec/src/enums/definitions.json)
2. Заменить `Base/Xrpl.BinaryCodec/Enums/definitions.json`
3. Запустить генератор:
   ```bash
   dotnet run --project Tools/GenerateEnums
   ```
4. Проверить вывод — генератор покажет все отличия
5. Запустить тесты:
   ```bash
   dotnet test Tests/Xrpl.BinaryCodec.Test
   ```

## Добавление нового типа данных

Если в `definitions.json` появился новый тип (например `UInt128` с ordinal = 27):

### 1. Класс сериализации

Создать `Base/Xrpl.BinaryCodec/Types/Uint128.cs`, реализующий `ISerializedType`:

```csharp
public class Uint128 : ISerializedType
{
    public void ToBytes(IBytesSink sink) { ... }
    public JsonNode ToJson() { ... }
    public static Uint128 FromJson(JsonNode token) { ... }
    public static Uint128 FromParser(BinaryParser parser, int? hint = null) { ... }
}
```

### 2. FieldType enum

Добавить в `Base/Xrpl.BinaryCodec/Enums/FieldType.cs`:

```csharp
public static readonly FieldType Uint128 = new FieldType(nameof(Uint128), 27);
```

### 3. Typed Field class

Создать `Base/Xrpl.BinaryCodec/Enums/Uint128Field.cs`:

```csharp
public class Uint128Field : Field
{
    public Uint128Field(string name, int nthOfType,
        bool isSigningField = true, bool isSerialised = true) :
            base(name, nthOfType, FieldType.Uint128,
                isSigningField, isSerialised) { }
}
```

### 4. Dispatch-таблица

В `Base/Xrpl.BinaryCodec/Types/StObject.cs` добавить в `DispatchTable`:

```csharp
[FieldType.Uint128] = new BuildFrom(Uint128.FromJson, Uint128.FromParser),
```

### 5. Индексатор (опционально)

В `StObject.cs` для типобезопасного доступа:

```csharp
public Uint128 this[Uint128Field f]
{
    get { return (Uint128)Fields[f]; }
    set { Fields[f] = value; }
}
```

### 6. Маппинги генератора

В `Tools/GenerateEnums/Program.cs` добавить в оба словаря:

```csharp
// TypeToFieldClass
["UInt128"] = "Uint128Field",

// TypeToFileName
["UInt128"] = "Uint128",
```

### 7. Перегенерировать

```bash
dotnet run --project Tools/GenerateEnums --force
```

Генератор автоматически создаст `Field.Uint128.Generated.cs` со всеми полями этого типа.

## Архитектура partial-классов

Каждый класс (`EngineResult`, `TransactionType`, `LedgerEntryType`, `Field`) разделён на:

- **Ручной файл** — инфраструктура: конструктор, метод `Add`, свойство `Values`, логика
- **Генерируемый `.Generated.cs`** — только `static readonly` объявления

Это позволяет обновлять данные из `definitions.json` без потери ручного кода.

## Валидация

Генератор автоматически проверяет:
- Дубликаты `nth` в пределах одного типа (конфликт — два поля с одним порядковым номером)
- Неизвестные типы полей (не зарегистрированные в `TypeToFieldClass`)

Предупреждения выводятся в `stderr` с префиксом `!`.
