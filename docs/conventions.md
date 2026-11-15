# Code conventions

Layout, naming and code style the owner expects in `backend/`. They follow the sibling repository profit-fox
(`github.com/dmitryshelamov/profit-fox`, `docs/conventions.md`) unless this file says otherwise. Where a check enforces a rule
(`.editorconfig`, `backend/.globalconfig`, `backend/Directory.Build.targets`, `.githooks/pre-commit`, CI), the check is the source of
truth.

## Reference repositories

Before choosing a library or a pattern, look at how the owner's sibling repositories already solve it:

- **profit-fox** (`~/RiderProjects/profit-fox`, `github.com/dmitryshelamov/profit-fox`): the stack and the exchange adapters.
  Packages and their versions come from its `backend/Directory.Packages.props` (CryptoClients.Net for Bybit, Npgsql, Dapper,
  FluentMigrator, Testcontainers), the TimescaleDB image tag from its `docker-compose.yml`; the Bybit adapter follows
  `ProfitFox.Infrastructure/Exchanges/Bybit`.
- **profit-fox-old** (`~/RiderProjects/profit-fox-old`, `gitlab.com/dmitry_shelamov/profit-fox`): migrations
  (`Persistence/Postgres/FluentMigrator`), the test application (`ProfitFoxTestApplication : WebApplicationFactory<Program>`), the
  generators of domain objects in `Tests.Shared/Generators` and the names of ports and contracts (below). Its MediatR transaction
  behavior is not taken: see Application.

A library neither of them uses is a question to the owner, with a recommendation, not a choice to make alone. Where the two differ
and this file is silent, ask which to follow.

## Domain (`ProfitFox.MarketData.Domain`)

- Areas: `Instruments` (the catalog of exchange instruments), `Candles` (the minute candles of the instruments and how far storage holds them),
  `OpenInterest` (the open interest of the instruments, a point every five minutes, as far as storage holds it), `Funding` (the
  funding rates of the instruments, point by point, and the payments their exchange settled, as far as storage holds them),
  `Liquidations` (the liquidations of the instruments, summed by the minute and by the side of the liquidated positions),
  `LongShortRatio` (the shares of the accounts net long and net short in the instruments, a point every five minutes, as far as
  storage holds it).
- `Entities/<Area>` holds entities, `Entities/<Area>/Enums` the enums and their pure extensions (`BybitContractTypeExtensions`).
  `Entities/Base` holds `AggregateRoot`; `Events` holds `IDomainEvent`. Both are the shared base copied from profit-fox and stay
  even while no aggregate raises an event.
- `ValueObjects/<Area>` holds value objects (`BybitListedInstrument`, `InstrumentRules`). An abstract base of an area goes to
  `<Area>/Base` inside that folder (`ValueObjects/Instruments/Base`), never to a top-level `Base`.
- `Repositories` holds storage ports, named `I<Name>Repository`.
- An entity is a `sealed class` that derives from `AggregateRoot`: get-only properties or `private set`, a private constructor and
  a static factory (`Instrument.Create(id, listing, now)`); state changes through its methods. The id comes from the caller.
  A repository brings a stored entity back through `Restore(...)`, which takes every stored value and runs no behaviour, so loading
  never changes state or raises an event.
- A value object is a `sealed record` with get-only properties; its constructor checks what can really come wrong from the
  exchange (blank strings, a contract type of another category), not enum values out of range or nulls the compiler already rules out.
- A value object saved apart from a series carries its instrument (`OpenInterestPoint`, `FundingRatePoint`); one that only a series
  holds does not (`Candle`, `FundingPayment`).
- An exchange gets its own types (`BybitListedInstrument`, `BybitCategory`, `BybitInstrumentStatus`); a common base appears only
  when a second exchange brings a second type to extract it from. What the exchanges share is `ListedInstrument.Exchange`: code
  that must tell the exchanges apart reads it and does not test the type of a listing, except where it maps a listing to its own
  table or contract.
- The domain knows no wire codes: enums, not strings; the adapter of an exchange maps its codes.

## Application (`ProfitFox.MarketData.Application`)

- `Ports/<Area>` holds the ports to exchanges, named `I<Exchange><What>Client` (`IBybitInstrumentsClient`, `ITInvestSharesClient`), as
  profit-fox-old names them (`IMarketHistoryClient`); the adapter in `Infrastructure/Exchanges/<Exchange>` drops the `I`
  (`BybitInstrumentsClient`). Not `Service`: the SDK of T-Invest names its gRPC clients so (`MarketDataService.MarketDataServiceClient`).
  A port every exchange implements the same way is one interface, `IExchange<What>Client` (`IExchangeCandlesClient`), with an
  adapter per exchange (`BybitCandlesClient`, `TInvestCandlesClient`); its `Exchange` tells whose listings are its, so a handler
  picks the client by `listing.Exchange` and has no `switch`. Ports whose shapes differ stay apart (Bybit lists a category,
  T-Invest has none). A port to a stream of an exchange is named the same way (`IBybitCandleStreamClient`) and is held by a
  hosted service, not asked by a handler: a subscription outlives any request. What an exchange gives for several areas of the
  domain at once has an area of its own in Application, named after it: `Tickers` (`Ports/Tickers`, `Commands/Tickers`), as one ticker
  of Bybit tells both the open interest and the funding rate. What a port gives that the domain does not keep is a record next to the
  port (`BybitLinearTicker`, `LiveCandle`); so is what an adapter cannot give as the domain keeps it (`BybitOpenInterestPoint`: the
  point of the domain carries its instrument, which the adapter does not know) or gives with more than the domain keeps
  (`BybitFundingPaymentsPage`: the payments and whether the page was full).
- `Ports/Feeds` holds `ILiveFeed<T>`, the live feed of one kind of market data, whatever the kind: its handler publishes what it
  saved, a gRPC service subscribes. The feed of candles has a port of its own (`ILiveCandleFeed`): a forming candle is not saved and
  may be left out for a slow subscriber. Its subscription is still an `ILiveSubscription<LiveCandle>`, so one call serves every feed.
- A query whose answer is too large to hold is an `IStreamRequest` (`GetClosedCandlesQuery`), read with `ISender.CreateStream`;
  its handler reads storage row by row.
- `Exceptions` holds the failures a port may throw and a caller handles (`ExchangePausedException`, `TransientExchangeException`,
  `InstrumentNotFoundException`); a failure only an adapter sees stays in Infrastructure.
- No request runs in a transaction of the MediatR pipeline: a handler asks an exchange, which no open transaction should wait
  for. A handler saves through one call of a repository, and the repository makes that call whole in a transaction of its own
  (`PostgresTransactionExecutor`). A handler that must save through two calls together asks the executor for it. Series that are
  read apart are saved by calls of their own, each whole, with no executor (`CollectBybitTickersCommandHandler`).
- A command gives a named result (`LoadCandlesResult.MoreLeft`), not a bare `bool`.
- `Queries/<Area>` holds a query with what it asks by (`CandleWindow`, `CandleInterval`); a rule of the input is checked there, not
  in the controller. What the queries of several areas ask by the same way lives in `Queries` itself (`PointWindow`). A query
  only reads. Its handler lives next to it when the repositories of the domain answer it (`GetInstrumentsQueryHandler`), and in
  `Infrastructure/Persistence/Queries/Handlers/<Area>` when it needs SQL of its own (`GetCandlesQueryHandler`).
- `Contracts/<Area>` holds what a query gives and the API shows, named `<Name>Internal` as in profit-fox-old
  (`InstrumentInternal`), with converters from the domain in `Contracts/<Area>/Converters`.

## Infrastructure (`ProfitFox.MarketData.Infrastructure`)

- A FluentMigrator migration lives in `Persistence/Postgres/FluentMigrator/Migrations`, in a file named by its version and what it
  does in snake_case (`202611120001_create_instruments_table.cs`), so the folder lists them in the order they apply; the class keeps
  its PascalCase name (`CreateInstrumentsTableMigration`). It derives from `Migration` and undoes itself in `Down()`.
- Names of constraints are snake_case: `<table>_pk`, `<table>_<columns>_unique`.
- Enums are stored as `text` with the names of their members, not as Postgres `ENUM` types.
- `Observability` pushes the metrics over OTLP to the Prometheus of profit-fox (`MetricsRegistrar`, the `Metrics` section). A
  metric of an exchange keeps the meter, name and tags profit-fox gives it (`ProfitFox.Bybit`, `profitfox.bybit.rest.requests`),
  so one dashboard sums the load of both services on the IP they share; the service is told apart by its `job`. It is recorded where
  every call passes: a handler on every client of the HTTP factory for Bybit (`BybitRestMetricsHandler`), an interceptor of the gRPC
  channel for T-Invest (`TInvestMetricsInterceptor`).
- The failures the application tells by an exception are answered in one place, `ApplicationExceptionHandler` of Presentation
  (404 for an unknown instrument, 503 for an exchange that holds the requests or is unavailable); a controller catches only what
  means something of its own there.
- A hosted service lives in `BackgroundServices` with its settings (`InstrumentSyncSettings`, the `InstrumentSync` section of
  `appsettings.json`), bound with `AddOptions().Bind().Validate().ValidateOnStart()` and read through `IOptionsMonitor` before every
  run, so a saved change applies from the next run without a restart; a broken change is logged and the last valid settings stay
  (`CurrentOrLastValid`). Each request goes in a scope of its own through `ISender` (`scopes.SendAsync`); a failure is logged and
  the service goes on. A service that loads history page by page for many instruments goes in rounds of `Loading`, which holds
  what such loadings share: the parallelism, the pause of an exchange, an exchange that is unavailable, an instrument that failed. `BackgroundServicesRegistrar` registers the services with the checks of their sections; an exchange is
  switched by `ExchangeSyncSettings`. `MarketDataTestApplication` leaves the services out.

## Presentation (`ProfitFox.MarketData.Presentation`)

- `Controllers` holds the REST API. `Protos` holds the gRPC contracts, one `.proto` a service, with `csharp_namespace`
  `ProfitFox.MarketData.Presentation.Protos`; `GrpcServices` holds their implementations, named `<Service>Service`
  (`CandleFeedService`). A contract speaks of what a consumer needs to know and names no type of the code. gRPC needs HTTP/2,
  which has an endpoint of its own in the `Kestrel` section of `appsettings.json`.
- Decimal numbers go over gRPC as text, exactly as stored: protobuf has no decimal type.
- Every kind of market data the service holds is given two ways: a gRPC feed, through which other services get it in real time,
  and a request for its history over a period, REST by instrument until a consumer needs more than pages give. A new kind brings
  both in its slice, or says which one is left for later. A feed is a service of its own (`OpenInterestFeed`, `FundingRateFeed`,
  `FundingPaymentFeed`, `LiquidationFeed`, `LongShortRatioFeed`), tells live what is stored and catches up from storage with
  `since`; the forming candle of `CandleFeed` is the one thing told that is not stored. What the calls of every feed do the same
  way, the candles among them, is `FeedCall`.
- The contracts share one package, so a message is named after its feed: `SubscribeOpenInterestRequest`, `OpenInterestUpdate`.
  `candle_feed.proto`, the first contract, keeps its `SubscribeRequest`: a new name would change the generated code of its consumers.
- What a handler publishes to a feed is made by the converter of the contract (`Contracts/<Area>/Converters`), as what a query gives.

## Files and code style

- One top-level type per file, named after the type (a migration is named by its version, see above); namespaces follow folders.
- In a type: properties first, then constructors, then methods. Among the members of one kind, what implements an interface comes
  first (`Dispose`, `DisposeAsync`, `MoveNext`, a property like `Current`), then the rest.
- An XML doc `<summary>` is always on three or more lines, the text indented by 5 spaces, even for one sentence:

  ```csharp
  /// <summary>
  ///     Step of the price.
  /// </summary>
  ```

- `var` only when the type is evident from the right side: a `new`, a cast, a factory or a conversion whose name tells the type
  (`FundingPaymentSeries.Create(...)`, `decimal.Parse(...)`, `ToString(...)`); the explicit type otherwise. Explicit `new Foo(...)`,
  never `new(...)`. Where Rider and the build disagree on what is evident, the build decides: it fails on `IDE0007` and `IDE0008`.
- The format is the one Rider's "Reformat and Cleanup Code" gives with the `.editorconfig` of the repository.
  `scripts/verify-format.sh` checks only a part of it, so code is written this way from the start:
  - A statement stays on one line while it fits `max_line_length`, 170 characters. It is broken only when it does not fit, and then
    after the opening bracket, an argument a line. A line that fits is not broken to look shorter.
  - In a call with several arguments, a literal argument is named when it is a number, `true` or `false`, `null` or `default`:
    `new Candle(Minute, open: 5m, high: 8m, ...)`, `Enumerable.Range(start: 0, count)`, `Answer(op, requestId, success: true, "pong")`.
    A string literal and any other expression are not named: `new FundingPayment(time, rate)`, never `rate: rate`. The only argument
    of a call is not named: `TimeSpan.FromSeconds(30)`.
  - No brackets the precedence of the operators does not need: `time.UtcTicks - time.UtcTicks % Step.Ticks`.
  - No namespace in front of a name in code: a `using`, or an alias where two names clash (`using Enum = System.Enum;`).
- Every attribute is in brackets of its own, never `[A, B]`; attributes on one line, as on a parameter, have a space between them:
  `[FromQuery] [BindRequired] string interval`. Rider keeps this (`.editorconfig`). The formatter of Roslyn deletes that space and
  has no option for it, so the format is checked by `scripts/verify-format.sh`, which is `dotnet format --verify-no-changes` that
  lets exactly that report pass; a plain `dotnet format` would rewrite such lines.

## Tests

- `Tests.Unit` tests Domain and Application only and references nothing above Application; ports are NSubstitute substitutes.
- Infrastructure and Presentation are tested in `Tests.Integration`, against a real TimescaleDB (Testcontainers) and with HTTP to
  an exchange answered by `StubHttpHandler`; a stream of Bybit is answered by `FakeBybitSocketServer`, its sockets on a local port.
  A gRPC service is called with its generated client over the app in memory (`ResponseVersionHandler`).
- `Tests.Shared` holds what both test projects use: generators of domain objects in `Generators/Domain/<Entities|ValueObjects>/<Area>`,
  static `<Type>Generator` classes with `Generate…` methods whose parameters default to a valid object
  (`InstrumentGenerator.Generate(listing, firstSeenAt, id)`, `BybitListedInstrumentGenerator.GenerateLinear("ETH")`). A test passes
  only the values it checks. A helper only one test project uses lives in that project.
- `Tests.Unit` and `Tests.Integration` mirror the folders of `src` (`Tests.Unit/Domain/Entities/Instruments/InstrumentTests.cs`,
  `Tests.Integration/Infrastructure/Exchanges/Bybit/BybitInstrumentsTests.cs`).
- NUnit with the constraint model (`Assert.That`, `Assert.Multiple`).
- In a fixture: `[SetUp]` and `[TearDown]` first, then the fields, then the helpers and the `[TestCase]` methods, then the `[Test]`
  methods in the alphabetical order of their names. A new test goes where its name puts it, not at the end.
