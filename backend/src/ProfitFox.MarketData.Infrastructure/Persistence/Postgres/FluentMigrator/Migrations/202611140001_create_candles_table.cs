using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611140001)]
public sealed class CreateCandlesTableMigration : Migration
{
    // The minute candles of every instrument as its exchange gave them: unconstrained numeric keeps every value exactly, a turnover is
    // null where the exchange does not tell it (T-Invest), a buy volume where the sides of the trades are not known. Daily chunks, each
    // compressed once it is 7 days old; a segment per instrument, so a read of one instrument unpacks only its own.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE EXTENSION IF NOT EXISTS timescaledb;

            CREATE TABLE candles (
                instrument_id uuid NOT NULL,
                open_time timestamp with time zone NOT NULL,
                open numeric NOT NULL,
                high numeric NOT NULL,
                low numeric NOT NULL,
                close numeric NOT NULL,
                volume numeric NOT NULL,
                turnover numeric NULL,
                buy_volume numeric NULL,
                CONSTRAINT candles_pk PRIMARY KEY (instrument_id, open_time),
                CONSTRAINT candles_instrument_id_fk FOREIGN KEY (instrument_id) REFERENCES instruments (id)
            );

            SELECT create_hypertable('candles', by_range('open_time', INTERVAL '1 day'));

            ALTER TABLE candles SET (
                timescaledb.compress,
                timescaledb.compress_segmentby = 'instrument_id',
                timescaledb.compress_orderby = 'open_time'
            );

            SELECT add_compression_policy('candles', INTERVAL '7 days');
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE candles;");
    }
}