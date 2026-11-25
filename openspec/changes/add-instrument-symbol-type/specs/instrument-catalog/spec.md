# Spec Delta

## MODIFIED Requirements

### Requirement: Инструменты Bybit в каталоге

Система SHALL брать с Bybit пары spot (одним ответом) и контракты linear (постранично до 1000, пока Bybit отдаёт курсор), с
шагом цены, шагом и минимумом количества, минимальной суммой ордера, типом инструмента, а для linear — типом контракта и
временем запуска. Тип инструмента — акция, фонд, сырьё, валюта, токенизированная акция, зона инноваций или зона
приключений; пустой тип и тип, которого каталог не знает, MUST давать «нет типа», а не ошибку синхронизации.
Категории Bybit, кроме spot и linear, каталог не знает: запрос синхронизации с другой категорией MUST отклоняться как
некорректный.

#### Scenario: Неподдерживаемая категория

- **WHEN** запрошена синхронизация категории `Inverse` Bybit
- **THEN** система отвечает `400` без запроса к Bybit

#### Scenario: Контракт на акцию

- **WHEN** Bybit листингует linear `TSLLUSDT` с типом `stock`, а `SPYUSDT` — с типом `ETF`
- **THEN** в каталоге у `TSLLUSDT` тип «акция», у `SPYUSDT` — «фонд»

#### Scenario: Тип пуст

- **WHEN** Bybit листингует linear `BTCUSDT` с пустым типом
- **THEN** в каталоге у `BTCUSDT` нет типа

#### Scenario: Незнакомый тип

- **WHEN** Bybit листингует контракт с типом `bond`, которого каталог не знает
- **THEN** синхронизация проходит, и у этого контракта нет типа

#### Scenario: Тип сменился

- **WHEN** в каталоге у контракта тип «зона инноваций», а Bybit листингует его с типом `stock`
- **THEN** после синхронизации у того же инструмента тип «акция»

### Requirement: Чтение каталога

Система SHALL отдавать по `GET api/instruments` все инструменты каталога, листингованные и делистингованные, по
`GET api/instruments/bybit` — только инструменты Bybit, по `GET api/instruments/tinvest` — только акции T-Invest. Листинг
инструмента в ответе MUST содержать поле `exchange` со значением `bybit` или `tinvest`. Листинг Bybit MUST содержать поле
`symbolType`: `Stock`, `Etf`, `Commodity`, `Forex`, `XStocks`, `Innovation`, `Adventure` или `null`, если типа нет.

#### Scenario: Каждая биржа отдаёт свои инструменты

- **WHEN** в каталоге есть `BTCUSDT` Bybit и `SBER` T-Invest
- **THEN** `GET api/instruments/bybit` отдаёт только `BTCUSDT`, а `GET api/instruments/tinvest` — только `SBER`

#### Scenario: Тип в ответе

- **WHEN** в каталоге linear `TSLLUSDT` с типом «акция» и linear `BTCUSDT` без типа
- **THEN** `GET api/instruments/bybit` отдаёт у `TSLLUSDT` `"symbolType": "Stock"`, у `BTCUSDT` — `"symbolType": null`
