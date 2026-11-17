# Tasks

## 1. Контракты

- [x] 1.1 Добавить в `Protos/candle_feed.proto` `CandleInterval`, `TimeRange`, `InstrumentCoverage`, `CandleHistoryRequest`,
  `CandleHistoryItem` и `rpc History` с комментарием контракта: окно только прошедших дней, покрытие до строк, окончательное
  покрытие Bybit (`covered ∩ settled`), порядок время → инструмент, хэш по разобранным полям, только листингованные
  инструменты, коды `INVALID_ARGUMENT`/`NOT_FOUND`/`UNAVAILABLE`, ожидание очереди; проверка — `dotnet build` проходит,
  `SubscribeRequest` и `CandleUpdate` не изменились (`git diff` по их полям пуст)
- [x] 1.2 Добавить `History` с запросом и `oneof`-ответом в `open_interest_feed.proto`, `long_short_ratio_feed.proto`,
  `funding_payment_feed.proto`, импортируя `candle_feed.proto` для `Exchange`, `InstrumentCoverage`, `TimeRange`; проверка —
  `dotnet build` проходит, сгенерированные клиенты всех четырёх лент имеют метод `History`

## 2. Application: окна и запросы

- [x] 2.1 Добавить в `Queries` `HistoryWindow` (`from` < `to` ≤ начала дня UTC переданного `now`) и в `Queries/Candles`
  `CandleHistoryWindow` (интервал, выравнивание по UTC), нарушение — `ArgumentException`; проверка — unit-тесты на окно в
  сегодня, ровно до полуночи, задом наперёд, `1h` с `10:30`, `1d` не с полуночи
- [x] 2.2 Добавить для свечей, открытого интереса, лонг/шорт и выплат `Get<Kind>HistoryCoverageQuery` (инструменты, биржи,
  окно → покрытие в порядке `instrument_id`) и `Get<Kind>HistoryQuery : IStreamRequest` (покрытие и окно → строки), их
  контракты в `Contracts/<Area>` (`InstrumentCoverageInternal` общий в `Contracts`); проверка — `dotnet build` проходит

## 3. Infrastructure: чтение хранилища

- [x] 3.1 Обработчик покрытия свечей в `Persistence/Queries/Handlers/Candles`: листингованные инструменты запроса и бирж, Bybit —
  `covered * settled`, T-Invest — `covered`, обрезка по окну, `ORDER BY instrument_id`, `InstrumentNotFoundException` на чужой
  `id`; проверка — интеграционные тесты на сценарии «Окно покрыто частично», «Вчерашний день ещё не разобран», «Акция T-Invest»,
  «Весь рынок одной биржи», «Делистингованный назван явно», «Неизвестный инструмент»
- [x] 3.2 Обработчики покрытия открытого интереса, лонг/шорт и выплат: `[covered_from, covered_until)` ряда по окну, пустое
  покрытие без ряда; проверка — интеграционные тесты «Пара spot в истории открытого интереса», «Окно шире покрытия ряда»
- [x] 3.3 Обработчик строк свечей: дни UTC окна с непустым покрытием, одна выборка `QueryUnbufferedAsync` на день по всем
  инструментам через `unnest` покрытия, минуты по `@>`, интервалы — агрегаты `GetCandlesQueries.Buckets` с `HAVING` по покрытию
  свечи, `ORDER BY` время, `instrument_id`; на соединении `max_parallel_workers_per_gather = 0` и `work_mem = '256MB'`;
  проверка — интеграционные тесты «Пять минут из минут», «Минута без buy volume», «Час покрыт не целиком», «Загрузка во время
  чтения», «Две пары за два часа» и окно в три дня, где строки идут день за днём
- [x] 3.4 Обработчики строк открытого интереса, лонг/шорт и выплат по той же схеме; проверка — интеграционные тесты «Точки
  открытого интереса за окно», порядок двух инструментов в одно время, точки вне покрытия не приходят
- [x] 3.5 Проверить, что обработчики строк не держат окно в памяти: тест читает несколько дней и принимает строки по одной,
  проверяя, что выборка следующего дня начинается после того, как отданы строки предыдущего

## 4. Presentation: вызовы `History`

- [x] 4.1 Добавить `HistoryReadSettings` (`MaxConcurrentReads`, `Validate` > 0, `ValidateOnStart`), секцию `HistoryRead` в
  `appsettings.json` и `HistoryReadGate` (предел из `IOptionsMonitor` при каждом вызове, ожидание с отменой); проверка —
  unit-тесты «Третий вызов ждёт», «Ожидающий вызов отменён», новый предел с ближайшего освобождения; тест настроек — приложение
  с `MaxConcurrentReads` `0` не стартует
- [x] 4.2 Добавить `HistoryCall` в `GrpcServices`: очередь, покрытие, затем строки, `ArgumentException` → `INVALID_ARGUMENT`,
  `InstrumentNotFoundException` → `NOT_FOUND`, неизвестная биржа и `CANDLE_INTERVAL_UNSPECIFIED` → `INVALID_ARGUMENT`,
  остановка приложения → `UNAVAILABLE` для идущих и ожидающих; проверка — интеграционные тесты кодов и «Приложение
  останавливается во время чтения»
- [x] 4.3 Реализовать `History` в `CandleFeedService`, `OpenInterestFeedService`, `LongShortRatioFeedService`,
  `FundingPaymentFeedService`, строки — через существующие конвертеры сообщений лент; проверка — интеграционные тесты в
  `Presentation/GrpcServices` через клиента gRPC: «Свеча истории», «Три года рынка» (окно 1095 дней принимается),
  «Повтор через неделю» (два чтения одного покрытого окна дают одинаковые сообщения после сохранения новых минут и более
  старой истории), покрытие приходит раньше первой строки
- [x] 4.4 Обновить `docs/conventions.md` (раздел Presentation: история за период — `History` в ленте, когда потребителю нужен
  рынок или окно больше страниц; общие сообщения покрытия в `candle_feed.proto`) и README, если он перечисляет вызовы gRPC;
  проверка — текст описывает методы так, как они реализованы

## 5. Проверка целиком

- [x] 5.1 `dotnet build`, `dotnet test` и `scripts/verify-format.sh backend/ProfitFox.MarketData.sln` проходят;
  `openspec validate add-history-stream --strict` проходит

## Workflow follow-up

- Деплой — только по слову владельца.
- После деплоя, с согласия владельца: замер на soyo-02 — `History` свечей `5m` и `1h` всего рынка за месяц и за всё покрытое,
  время, load average и темп загрузки истории при одном и двух чтениях; по результату решить, нужны ли агрегаты и менять ли
  `MaxConcurrentReads`.
- Архив изменения после ревью и слияния.
