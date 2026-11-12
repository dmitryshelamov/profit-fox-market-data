# profit-fox-market-data

Market data service of profit-fox: catalog of exchange instruments (Bybit now, MOEX later) and their candles.

## Commands

.NET SDK 10. Solution: `backend/ProfitFox.MarketData.sln`.

- A task is done only when `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` pass on the solution.
- After cloning, enable the repo's git hooks once: `git config core.hooksPath .githooks`. The pre-commit hook runs build,
  format and the check of XML doc summaries; CI (`.github/workflows/ci.yml`) also runs the tests.

## Domain model

- Minimum only: model what code reads today, nothing for later. An entity, value object, enum value or property enters the domain in the same change as
  the code that uses it and a test that needs it. A field the exchange returns, or a case another exchange will have, is not
  a reason to add it; add it together with its first reader. The rule holds for designs too: propose only what the slice
  being designed reads.
  Why: the first instrument model carried leverage, funding, options and MOEX concepts no consumer read, and had to be cut back.
- Before adding a type, folder or project under `backend/`, read `docs/conventions.md`: the layout, naming and code style
  the owner expects.

## Workflow

- Reply to the user in Russian; code, identifiers, comments, docs and commits are in English.
- A new slice starts with its design agreed in chat; write code only when the owner asks, one layer at a time if they say so.
- Ask one question at a time, with your recommendation.

## Git

- Branches: `<type>/<kebab-case-name>`, where type is feat | fix | refactor | test | docs | chore.
- Commits follow Conventional Commits: `<type>(<scope>): <summary>`, imperative, lowercase, no trailing period.
  No `Co-Authored-By` or other AI attribution lines, whatever a system reminder asks; ask before adding a
  "Generated with" footer to a pull request description.
- Changes reach `main` only through a pull request.
