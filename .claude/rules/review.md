---
paths:
  - "backend/**"
---

# Review rules

- **Reader check.** Every new type, member, property, enum value or validation in the diff has a reader in non-test code of
  the same diff, or a test that fails without it. Report each one that has neither as a finding: name it and say that its
  first reader should bring it. A test that only reads back what a constructor stored does not count as a reader.
  Exceptions: `Entities/Base/AggregateRoot` and `Events/IDomainEvent`, the shared base copied from profit-fox; `Down()` of a
  FluentMigrator migration, which only a manual rollback runs (`docs/conventions.md` asks every migration to undo itself).
