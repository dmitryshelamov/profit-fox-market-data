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
  (`Persistence/Postgres/FluentMigrator`), the MediatR transaction behavior (`Mediatr/Behaviors/PostgresTransactionBehavior`), the
  test application (`ProfitFoxTestApplication : WebApplicationFactory<Program>`) and the generators of domain objects in
  `Tests.Shared/Generators`.

A library neither of them uses is a question to the owner, with a recommendation, not a choice to make alone. Where the two differ
and this file is silent, ask which to follow.

## Domain (`ProfitFox.MarketData.Domain`)

- Areas: `Instruments` (the catalog of exchange instruments).
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
- An exchange gets its own types (`BybitListedInstrument`, `BybitCategory`); a common base appears only when a second exchange
  brings a second type to extract it from.
- The domain knows no wire codes: enums, not strings; the adapter of an exchange maps its codes.

## Infrastructure (`ProfitFox.MarketData.Infrastructure`)

- A FluentMigrator migration lives in `Persistence/Postgres/FluentMigrator/Migrations`, in a file named by its version and what it
  does in snake_case (`202611120001_create_instruments_table.cs`), so the folder lists them in the order they apply; the class keeps
  its PascalCase name (`CreateInstrumentsTableMigration`). It derives from `Migration` and undoes itself in `Down()`.
- Names of constraints are snake_case: `<table>_pk`, `<table>_<columns>_unique`.
- Enums are stored as `text` with the names of their members, not as Postgres `ENUM` types.

## Files and code style

- One top-level type per file, named after the type (a migration is named by its version, see above); namespaces follow folders.
- In a type: properties first, then constructors, then methods.
- An XML doc `<summary>` is always on three or more lines, the text indented by 5 spaces, even for one sentence:

  ```csharp
  /// <summary>
  ///     Step of the price.
  /// </summary>
  ```

- `var` only when the type is evident from the right side; explicit `new Foo(...)`, never `new(...)`.

## Tests

- `Tests.Unit` tests Domain and Application only and references nothing above Application; ports are NSubstitute substitutes.
- Infrastructure and Presentation are tested in `Tests.Integration`, against a real TimescaleDB (Testcontainers) and with HTTP to
  an exchange answered by `StubHttpHandler`.
- `Tests.Shared` holds what both test projects use: generators of domain objects in `Generators/Domain/<Entities|ValueObjects>/<Area>`,
  static `<Type>Generator` classes with `Generate…` methods whose parameters default to a valid object
  (`InstrumentGenerator.Generate(listing, firstSeenAt, id)`, `BybitListedInstrumentGenerator.GenerateLinear("ETH")`). A test passes
  only the values it checks. A helper only one test project uses lives in that project.
- `Tests.Unit` and `Tests.Integration` mirror the folders of `src` (`Tests.Unit/Domain/Entities/Instruments/InstrumentTests.cs`,
  `Tests.Integration/Infrastructure/Exchanges/Bybit/BybitExchangeInstrumentsTests.cs`).
- NUnit with the constraint model (`Assert.That`, `Assert.Multiple`).
