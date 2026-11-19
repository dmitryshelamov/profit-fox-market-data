# Timing of the History calls

Times the gRPC `History` calls of one instrument as the hypothesis bench makes them (4h candles and funding payments from
`2023-01-01`, one week of 1m candles) and hashes every answer, so a change of the service can be checked to be faster and to
tell exactly the same. The data are synthetic and the same on every run, see `seed.sql`.

## Locally

1. A TimescaleDB of its own, apart from the development database on `5433`. The seed compresses about 2 400 chunks in one
   transaction, which needs more locks than the default:

   ```bash
   docker run -d --name pfmd-history-timing -p 127.0.0.1:5434:5432 -e POSTGRES_DB=marketdata -e POSTGRES_USER=marketdata -e POSTGRES_PASSWORD=marketdata timescale/timescaledb:2.30.2-pg17 -c max_locks_per_transaction=4096
   ```

2. The service from the working tree, in a terminal of its own; it migrates the database and serves gRPC on `localhost:5237`:

   ```bash
   scripts/history-timing/run-service.sh
   ```

3. The data, once the service answers `http://localhost:5236/health` (about a minute):

   ```bash
   docker exec -i pfmd-history-timing psql -U marketdata -d marketdata -v ON_ERROR_STOP=1 < scripts/history-timing/seed.sql
   ```

   It ends with `candle_chunks` 2429 and `minutes` 6067709.

4. The timing, five runs of each read after a warm-up:

   ```bash
   dotnet run -c Release --project scripts/history-timing/HistoryTiming -- http://localhost:5237 00000000-0000-0000-0000-000000000001 5
   ```

To time another version of the service, stop it, check the version out and start step 2 again; the data stay.

## Against a deployed service

The client reads only, through the same `History` calls the bench makes. Give it the gRPC address of the service and the id of
an instrument from its catalog:

```bash
dotnet run -c Release --project scripts/history-timing/HistoryTiming -- http://<host>:<grpc port> <instrument id> 5
```

The hashes then depend on the stored data, so they compare two versions only while the coverage of the instrument in the
windows stays the same: the windows end on `2026-05-16`, long settled.
