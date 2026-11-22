# Design

## Context

Мотив — в proposal.md («Why»). Как устроено сейчас:

- `NpgsqlDataSource` создаётся один раз из `ConnectionStrings:MarketData` (`PostgresRegistrar`), таймаута в строке нет, значит
  у каждой команды 30 с Npgsql. Строка подключения лежит в `.env.prod` на сервере и читается только при старте.
- Вызов `History` — это `HistoryCall.ServeAsync` в Presentation: место в `HistoryReadGate`, потом обработчик покрытия
  (`HistoryCoverageReader`: проверка неизвестных `id` и запрос покрытия), потом обработчик строк (`HistoryConnection.PrepareAsync`
  — `SET` сессии, затем `HistoryRows.ReadAsync` по части окна). Все запросы идут через Dapper `CommandDefinition`.
- Секция `HistoryRead` (`MaxConcurrentReads`) — класс `HistoryReadSettings` в Presentation, регистрируется в `Program.cs`
  через `AddOptions().Bind().Validate().ValidateOnStart()` и читается `HistoryReadGate` через `IOptionsMonitor` при каждом вызове.
  `config/market-data.json` на сервере (`SettingsFile`) перекрывает остальные источники и перечитывается при каждом сохранении.
- При таймауте Npgsql бросает `NpgsqlException` с `TimeoutException` внутри; `HistoryCall` его не ловит, и gRPC отдаёт `UNKNOWN`.

## Goals / Non-Goals

**Goals:**

- Таймаут запросов истории — отдельный от записи, правится в `config/market-data.json` без перезапуска.
- Таймаут истории даёт потребителю `UNAVAILABLE`.

**Non-Goals:**

- Причина всплесков (догрузка buy volume, блокировки, диск) — не здесь; журнал ожиданий блокировок в Postgres включается
  отдельно, если понадобится.
- Повтор чтения внутри сервиса: повторяет потребитель (стенд), он знает, что прочитал.
- Таймаут записи и REST: остаётся из строки подключения.

## Decisions

### Таймаут на каждую команду Dapper, а не в строке подключения

`commandTimeout` передаётся в `CommandDefinition` каждого запроса истории: `UnknownSql`, запрос покрытия, `ReadSettingsSql` и
каждая часть строк. Так значение берётся из настроек при каждом вызове и не трогает остальных пользователей того же пула.

Отвергнуто: `Command Timeout` в строке подключения — один на всё приложение, только после перезапуска и в файле с секретами;
второй `NpgsqlDataSource` для истории — второй пул соединений ради одного числа, и тоже без правки на ходу;
`SET statement_timeout` в сессии — Postgres отменил бы запрос сам, но Npgsql всё равно оборвёт ожидание через свои 30 с.

Npgsql считает таймаут на каждое ожидание ответа сервера, а не на весь запрос: часть, которая отдаёт строки, пока потребитель
их принимает, не обрывается из-за длины окна. Это и нужно: ограничено ожидание хранилища, а не чтение.

### Одна секция `HistoryRead`, класс настроек переезжает в Infrastructure

`StatementTimeoutSeconds` (целое, секунды — единица Npgsql; по умолчанию `120`) добавляется в ту же секцию `HistoryRead`: это
настройки одного и того же — чтения истории. Её читают двое: `HistoryReadGate` в Presentation и обработчики в Infrastructure.
Presentation зависит от Infrastructure, а не наоборот, поэтому `HistoryReadSettings` переезжает в Infrastructure
(`Persistence/Queries/Handlers/History`), публичным, и регистрируется там же, где регистрируются обработчики, с проверками
обоих свойств (`> 0`) и `ValidateOnStart`. `Program.cs` больше её не регистрирует.

Отвергнуто: второй класс в Infrastructure на ту же секцию — два места с одним именем секции и две проверки; передавать таймаут
в запросах Application (`GetCandleHistoryQuery` и др.) — тащит деталь хранилища в контракт Application.

### Значение читается обработчиком при его начале

Обработчики покрытия и строк четырёх лент получают `IOptionsMonitor<HistoryReadSettings>`, берут `CurrentValue` один раз в
начале и передают число в `HistoryCoverageReader`, `HistoryConnection` и `HistoryRows` параметром. Правка действует со
следующего вызова (спец «Ожидание хранилища»).

### Сломанная правка не ломает чтения

`HistoryReadSettings` регистрируется через `LastValidOptionsMonitor<T>.Register`, как настройки API бирж: правка на `0` пишется
в журнал один раз, вызовы идут с последними верными настройками, а сломанная секция при старте по-прежнему не даёт стартовать.
Сейчас сломанная правка `MaxConcurrentReads` бросала бы исключение из `CurrentValue` в каждом вызове — это поведение заодно
уходит (спец «Одновременные чтения»).

### Таймаут хранилища — исключение Application, статус ставит Presentation

В Application появляется `StorageTimeoutException` («хранилище не ответило вовремя»). Infrastructure переводит в него
`NpgsqlException` с `TimeoutException` внутри — в `HistoryCoverageReader`, `HistoryConnection` и `HistoryRows`.
`HistoryCall` ловит его рядом с `InstrumentNotFoundException` и отдаёт `UNAVAILABLE`. Presentation не знает про Npgsql.

`HistoryRows.ReadAsync` — асинхронный итератор, а C# не даёт `yield return` внутри `try` с `catch` (CS1626): перевод
исключения оборачивает `ExecuteReaderAsync` и каждый `ReadAsync` читателя, а `yield return` стоит вне `try`.

Отвергнуто: `DEADLINE_EXCEEDED` — в gRPC это срок, который поставил клиент, а здесь его нет; `INTERNAL` — говорит об ошибке
сервиса, а не о временной недоступности. Стенд уже считает `UNAVAILABLE` временным сбоем хранилища.

### Проверка в тестах блокировкой таблицы

Интеграционный тест держит в отдельном соединении `LOCK TABLE candles IN ACCESS EXCLUSIVE MODE`: запрос истории ждёт
блокировку, как ждал бы медленный диск. С ожиданием `1` с вызов завершается с `UNAVAILABLE`; с ожиданием `10` с и снятой через
2 с блокировкой — с `OK`. Правка на ходу проверяется записью файла настроек, как в `SettingsFileTests`.

## Risks / Trade-offs

- [Долгое ожидание держит место `HistoryReadGate`, пока хранилище не ответит] → предел мест и так мал (2), а потребитель один;
  значение можно снизить на ходу.
- [Npgsql после таймаута шлёт серверу отмену и ждёт её до `Cancellation Timeout`, так что вызов заканчивается чуть позже
  ожидания] → на `UNAVAILABLE` это не влияет; соединение области вызова закрывается вместе с ней.
- [`LastValidOptionsMonitor` лежит в `Exchanges`, а теперь им пользуется и история] → класс обобщённый; перенос в общее место
  — отдельная уборка, не в этом изменении.

## Migration Plan

1. Релиз и выкладка на soyo-02 обычным путём. Без правки файла настроек действует `120` с из `appsettings.json`.
2. При необходимости значение правится в `config/market-data.json` (`"HistoryRead": { "StatementTimeoutSeconds": … }`).
3. Откат — предыдущий образ; настройка в файле ему не мешает: неизвестное свойство секции он не читает.
