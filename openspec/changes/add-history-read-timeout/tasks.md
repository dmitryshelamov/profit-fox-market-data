# Tasks

Каждая группа закрывается, когда проходят `dotnet build`, `dotnet test` и `scripts/verify-format.sh backend/ProfitFox.MarketData.sln`.
Тесты пишутся раньше кода (TDD): сначала красный тест, потом код.

## 1. Секция `HistoryRead` в Infrastructure

- [x] 1.1 Перенести `HistoryReadSettings` из `Presentation/Settings` в `Infrastructure/Persistence/Queries/Handlers/History`
  (публичный), добавить `StatementTimeoutSeconds` (по умолчанию `120`, XML-summary: ожидание хранилища каждым запросом истории,
  читается при каждом вызове). Регистрировать секцию в Infrastructure (`AddOptions().Bind().Validate()` для обоих свойств
  `> 0` с текстами `HistoryRead:MaxConcurrentReads must be above zero.` / `HistoryRead:StatementTimeoutSeconds must be above
  zero.`, `ValidateOnStart`) и через `LastValidOptionsMonitor<HistoryReadSettings>.Register`; убрать регистрацию из
  `Program.cs`. Добавить `"StatementTimeoutSeconds": 120` в секцию `HistoryRead` `appsettings.json`. Проверка — существующие
  тесты `HistoryReadGateTests` и `FeedHistoryTests` проходят после правки `using`
- [x] 1.2 Тест в `FeedHistoryTests` (рядом с `An_app_whose_history_reads_none_at_once_does_not_start`): приложение с
  `HistoryRead:StatementTimeoutSeconds` = `0` не стартует. Проверка — тест проходит
- [x] 1.3 Тест в `SettingsFileTests`: при старте файл даёт `StatementTimeoutSeconds` `120`, затем файл с `0` — монитор
  `HistoryReadSettings` отдаёт прежние `120` и `MaxConcurrentReads`, а файл с `300` — отдаёт `300`. Проверка — тест проходит
  (сценарии «Правка на ходу» и «Сломанная правка на ходу»)

## 2. Ожидание хранилища в запросах истории

- [ ] 2.1 Добавить `StorageTimeoutException` в `Application/Exceptions` («The storage did not answer within {seconds} s.»,
  XML-summary). Проверка — сборка и проверка summary pre-commit hook проходят
- [ ] 2.2 Красный интеграционный тест в `FeedHistoryTests`: отдельное соединение берёт `LOCK TABLE candles IN ACCESS EXCLUSIVE
  MODE` в открытой транзакции; вызов `CandleFeed.History` у приложения с `StatementTimeoutSeconds` = `1` завершается с
  `UNAVAILABLE` за `Patience`. Второй тест: с `StatementTimeoutSeconds` = `10` и блокировкой, снятой через 2 с, вызов отдаёт
  покрытие и строки и завершается с `OK`. Проверка — первый тест падает на текущем коде (`UNKNOWN` или 30 с), второй проходит
- [ ] 2.3 Передать таймаут: обработчики покрытия и строк четырёх лент (`GetCandleHistory*`, `GetOpenInterestHistory*`,
  `GetLongShortRatioHistory*`, `GetFundingPaymentHistory*`) берут `IOptionsMonitor<HistoryReadSettings>.CurrentValue` один раз в
  начале и передают `StatementTimeoutSeconds` параметром в `HistoryCoverageReader.ReadAsync`, `HistoryConnection.PrepareAsync`
  и `HistoryRows.ReadAsync`, которые ставят его в `commandTimeout` каждого `CommandDefinition`. Проверка — тесты 2.2 и
  существующие тесты обработчиков истории проходят
- [ ] 2.4 Переводить `NpgsqlException` с `TimeoutException` внутри в `StorageTimeoutException` в `HistoryCoverageReader`,
  `HistoryConnection` и `HistoryRows` (в итераторе — вокруг `ExecuteReaderAsync` и `ReadAsync` читателя, `yield return` вне
  `try`, CS1626); `HistoryCall.ServeAsync` ловит его и бросает `RpcException(StatusCode.Unavailable, message)`, XML-комментарий
  `<exception>` дополняется. Проверка — первый тест 2.2 проходит
- [ ] 2.5 Тест-обработчика: запрос записи (например, `CandleSeriesRepository`) в той же области с `StatementTimeoutSeconds` = `1`
  под той же блокировкой ждёт дольше 1 с — то есть таймаут истории его не касается. Если тест выходит хрупким, заменить его
  проверкой, что `commandTimeout` ставится только в файлах `Persistence/Queries/Handlers/History` и обработчиках `*History*`
  (`git grep`). Проверка — тест или проверка проходит

## 3. Документация

- [ ] 3.1 README, «Settings without a restart»: строка таблицы `HistoryRead` (`MaxConcurrentReads`, `StatementTimeoutSeconds`) —
  «from the next call of `History`»; пример правки ожидания. Проверка — таблица совпадает с кодом регистрации
- [ ] 3.2 `docs/conventions.md`, абзац про `History`: таймаут запросов истории из `HistoryRead`, таймаут хранилища — `UNAVAILABLE`;
  «read a UTC day a statement» поправить на части окна до 64 инструменто-дней, как в спеке. Проверка — текст совпадает со
  спекой `history-stream`

## Workflow follow-up

- Владелец: ревью, PR в `main`, релиз и выкладка на soyo-02.
- После выкладки: вызов `History` свечей H0001-окна идёт, в журнале сервиса нет ошибок настроек; при необходимости значение
  правится в `config/market-data.json`.
- Архив изменения после мержа.
