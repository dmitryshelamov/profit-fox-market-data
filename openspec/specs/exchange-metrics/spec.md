# exchange-metrics Specification

## Purpose

Метрики вызовов API бирж: сколько запросов сервис шлёт на Bybit и T-Invest, чем они кончаются и сколько осталось от лимита
T-Invest. Имена и теги — те же, что у profit-fox, а сервис отличается своим `job`, поэтому дашборды Prometheus и Grafana на
soyo-03 складывают нагрузку обоих сервисов на общий IP. Архив сделок `public.bybit.com` и стримы не считаются.

## Requirements

### Requirement: Отправка метрик

Система SHALL отправлять метрики по OTLP (`http/protobuf`, накопительно) на адрес `Metrics:OtlpEndpoint` раз в
`Metrics:ExportInterval` (по умолчанию 5 секунд) под именем сервиса `profit-fox-market-data`, которое Prometheus делает меткой
`job`. Пустой адрес SHALL выключать отправку. Адрес не `http` и не `https` или интервал не больше нуля MUST останавливать
приложение при старте.

#### Scenario: Адреса нет

- **WHEN** `Metrics:OtlpEndpoint` пуст
- **THEN** метрики никуда не отправляются, и соединение не открывается

#### Scenario: Сломанная секция

- **WHEN** `Metrics:OtlpEndpoint` — `ftp://192.168.8.142/metrics`
- **THEN** приложение не стартует

### Requirement: Счётчик запросов к Bybit REST

Система SHALL считать каждую HTTP-попытку к `api.bybit.com`, повторы тоже, счётчиком `profitfox.bybit.rest.requests` метра
`ProfitFox.Bybit` с тегами `bybit.endpoint` (путь), `http.response.status_code` и `bybit.outcome`: `rate_limited` для HTTP 403 и
429 и для `retCode` 10006 в ответе 200, `error` для другого кода HTTP не 2xx, другого `retCode` и попытки без ответа (тогда без
кода HTTP), иначе `ok`. Запрос на другой адрес MUST не считаться. Счётчик MUST не менять ответ и ошибку запроса.

#### Scenario: Лимит в ответе 200

- **WHEN** Bybit отвечает 200 с `retCode` 10006 на `/v5/market/kline`
- **THEN** счётчик растёт на 1 с `bybit.endpoint=/v5/market/kline`, кодом 200 и `bybit.outcome=rate_limited`

#### Scenario: Обрыв соединения

- **WHEN** попытка к `/v5/market/tickers` падает без ответа
- **THEN** счётчик растёт на 1 с `bybit.outcome=error` без кода HTTP, а вызывающий получает ту же ошибку

#### Scenario: Архив сделок

- **WHEN** сервис скачивает файл с `public.bybit.com`
- **THEN** счётчик не меняется

### Requirement: Счётчик вызовов T-Invest и его лимиты

Система SHALL считать каждый unary-вызов T-Invest после его конца счётчиком `profitfox.tinvest.requests` метра
`ProfitFox.TInvest` с тегами `group` (`instruments` или `market_data`, по сервису вызова), `method` и `outcome`: `rate_limited`
для `RESOURCE_EXHAUSTED`, `error` для другой ошибки, иначе `ok`. Вызов, от которого отказался вызывающий, MUST не считаться.
Заголовки `x-ratelimit-limit` и `x-ratelimit-remaining` успешного ответа SHALL давать по группе датчики
`profitfox.tinvest.limit` (последний лимит) и `profitfox.tinvest.limit_remaining` (наименьший остаток с прошлого сбора; сбор
его забирает).

#### Scenario: Остаток лимита

- **WHEN** три ответа `GetCandles` приходят с лимитом 100 и остатками 90, 40 и 70
- **THEN** при сборе `profitfox.tinvest.limit` равен 100, а `profitfox.tinvest.limit_remaining` — 40, оба с `group=market_data`

#### Scenario: Лимит T-Invest

- **WHEN** `GetCandles` кончается `RESOURCE_EXHAUSTED`
- **THEN** счётчик растёт на 1 с `group=market_data`, `method=GetCandles`, `outcome=rate_limited`
