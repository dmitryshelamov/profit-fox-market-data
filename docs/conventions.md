# Code conventions

Layout, naming and code style the owner expects in `backend/`. They follow the sibling repository profit-fox
(`github.com/dmitryshelamov/profit-fox`, `docs/conventions.md`) unless this file says otherwise. Where a check enforces a rule
(`.editorconfig`, `backend/.globalconfig`, `.githooks/pre-commit`, CI), the check is the source of truth.

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
- A value object is a `sealed record` with get-only properties; its constructor checks what can really come wrong from the
  exchange (blank strings, a contract type of another category), not enum values out of range or nulls the compiler already rules out.
- An exchange gets its own types (`BybitListedInstrument`, `BybitCategory`); a common base appears only when a second exchange
  brings a second type to extract it from.
- The domain knows no wire codes: enums, not strings; the adapter of an exchange maps its codes.

## Files and code style

- One top-level type per file, named after the type; namespaces follow folders.
- In a type: properties first, then constructors, then methods.
- An XML doc `<summary>` is always on three or more lines, the text indented by 5 spaces, even for one sentence:

  ```csharp
  /// <summary>
  ///     Step of the price.
  /// </summary>
  ```

- `var` only when the type is evident from the right side; explicit `new Foo(...)`, never `new(...)`.

## Tests

- `Tests.Unit` mirrors the folders of `src` (`Tests.Unit/Domain/Entities/Instruments/InstrumentTests.cs`).
- NUnit with the constraint model (`Assert.That`, `Assert.Multiple`).
