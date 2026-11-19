-- Synthetic history for timing the History calls, on a database the service has migrated and nothing else has written to.
-- TGTUSDT is the pair timed: minutes from 2023-11-01 to 2026-05-16. O2USDT..O10USDT share its chunks for the last year, and
-- FILLUSDT has one candle a day from 2020-03-25, so the hypertable has about as many daily chunks as production. Every value
-- is a function of time, so every run seeds the same rows and the hashes of the answers repeat. Every chunk older than 7 days
-- is compressed, as the policy of production leaves them. Takes about a minute.
\timing on

CREATE TEMP TABLE seed (id uuid, symbol text, c_from timestamptz, c_until timestamptz, every interval);

INSERT INTO seed
VALUES ('00000000-0000-0000-0000-000000000001', 'TGTUSDT', '2023-11-01', '2026-05-16', '1 minute'),
       ('00000000-0000-0000-0000-0000000000ff', 'FILLUSDT', '2020-03-25', '2026-11-18', '1 day');

INSERT INTO seed
SELECT ('00000000-0000-0000-0000-0000000000' || lpad(g::text, 2, '0'))::uuid, 'O' || g || 'USDT', '2025-05-16', '2026-05-16', '1 minute'
FROM generate_series(2, 10) g;

INSERT INTO instruments (id, first_seen_at)
SELECT id, c_from FROM seed;

INSERT INTO bybit_listings (instrument_id, category, symbol, base_asset, quote_asset, status, tick_size, quantity_step, min_quantity, contract_type, launch_time)
SELECT id, 'linear', symbol, replace(symbol, 'USDT', ''), 'USDT', 'Trading', 0.0001, 0.1, 0.1, 'LinearPerpetual', c_from
FROM seed;

-- Loaded and settled over the whole range, so the coverage of Bybit is the range itself.
INSERT INTO candle_series (instrument_id, covered, settled)
SELECT id, tstzmultirange(tstzrange(c_from, c_until)), tstzmultirange(tstzrange(c_from, c_until))
FROM seed;

INSERT INTO candles (instrument_id, open_time, open, high, low, close, volume, turnover, buy_volume)
SELECT id, t, o, o + 0.02, o - 0.02, o + 0.01, v, round(v * 1.51, 4), round(v * 0.4, 1)
FROM (
    SELECT s.id, t,
           round((1.5 + sin(extract(epoch FROM t) / 86400.0))::numeric, 5) AS o,
           round((1000 + mod((extract(epoch FROM t)::bigint / 60) * 7919, 50000) / 10.0)::numeric, 1) AS v
    FROM seed s, generate_series(s.c_from, s.c_until - s.every, s.every) t
) minutes;

INSERT INTO funding_payment_series (instrument_id, covered_from, covered_until)
SELECT id, c_from, '2026-11-18' FROM seed WHERE symbol <> 'FILLUSDT';

INSERT INTO funding_payments (instrument_id, time, rate)
SELECT s.id, t, round((0.0001 * sin(extract(epoch FROM t) / 28800.0))::numeric, 8)
FROM seed s, generate_series(s.c_from, '2026-11-18'::timestamptz - interval '8 hours', '8 hours') t
WHERE s.symbol <> 'FILLUSDT';

SELECT count(*) AS compressed_chunks
FROM (SELECT compress_chunk(c, true) FROM show_chunks('candles', older_than => interval '7 days') c) compressed;

ANALYZE;

SELECT num_chunks AS candle_chunks FROM timescaledb_information.hypertables WHERE hypertable_name = 'candles';
SELECT count(*) AS minutes FROM candles;
