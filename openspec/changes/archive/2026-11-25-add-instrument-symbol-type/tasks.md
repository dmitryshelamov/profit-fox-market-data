# Tasks

Каждая группа закрывается, когда проходят `dotnet build`, `dotnet test` и `scripts/verify-format.sh backend/ProfitFox.MarketData.sln`.
Тесты пишутся раньше кода (TDD): сначала красный тест, потом код. Прежние тесты каталога не правятся; если задача требует
иного — остановиться и записать это в раздел «Открыто после применения» в конце этого файла.

## 1. Домен

- [x] 1.1 `BybitSymbolType` в `Domain/Entities/Instruments/Enums` (решение 1) и `SymbolType` у `BybitListedInstrument`
  (необязательный параметр конструктора, по умолчанию `null`, чтобы прежние вызовы не менялись). Проверить юнит-тестом:
  листинг с типом и без типа равны только при равных типах.

## 2. Разбор Bybit

- [x] 2.1 `BybitSymbolConverter` переводит `SymbolType` SDK у spot и linear (решение 1). Проверить юнит-тестами
  сценарии «Контракт на акцию» и «Тип пуст» требования «Инструменты Bybit в каталоге»; все семь значений SDK.
- [x] 2.2 Незнакомый тип. Проверить тестом клиента на записанном JSON-ответе Bybit с `"symbolType": "bond"`: список
  разобран целиком, у контракта нет типа (сценарий «Незнакомый тип»).

## 3. Хранение и ответ

- [x] 3.1 Миграция `bybit_listings.symbol_type text NULL` (решение 2); `InstrumentRepository` пишет и читает тип.
  Проверить интеграционным тестом сценарий «Тип сменился» требования «Инструменты Bybit в каталоге».
- [x] 3.2 `SymbolType` в `BybitListedInstrumentInternal` и в ответе (решение 3). Проверить интеграционным тестом
  сценарий «Тип в ответе» требования «Чтение каталога»; у T-Invest поля нет.

## 4. Стенд

- [x] 4.1 После выката на стенд сервиса данных: синхронизация linear и spot, `GET api/instruments/bybit` отдаёт у
  `TSLLUSDT` `"symbolType": "Stock"`, у `BTCUSDT` linear — `null`; число USDT-контрактов linear по каждому типу
  записать здесь с датой.
  2026-11-25, v17 на soyo-02: `POST api/instruments/bybit/sync` для `Linear` и `Spot` — `204`. У `BTCUSDT` (linear и
  spot) `"symbolType": null`; у `TSLLUSDT` — `"Etf"`, а не `"Stock"`: Bybit листингует его как фонд (Tesla с плечом),
  акция — `TSLAUSDT` с `"Stock"`. USDT-контракты linear `LinearPerpetual` в торговле: без типа 406, `Innovation` 110,
  `Stock` 248, `Etf` 62, `Commodity` 4, `Forex` 4 (всего 834). Spot: без типа 485, `XStocks` 11, `Adventure` 3.

## Открыто после применения
