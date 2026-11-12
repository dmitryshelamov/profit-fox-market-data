# profit-fox-market-data

## Domain model

- Minimum only: model what code reads today, nothing for later. An entity, value object, enum value or property enters the domain in the same change as
  the code that uses it and a test that needs it. A field the exchange returns, or a case another exchange will have, is not
  a reason to add it; add it together with its first reader.
  Why: the first instrument model carried leverage, funding, options and MOEX concepts no consumer read, and had to be cut back.
