using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611150004)]
public sealed class CreateLiquidationsTableMigration : Migration
{
    // The liquidations of every instrument summed by the minute and by the side of the liquidated positions: what was liquidated in
    // the base asset, what it was worth in the quote asset and how many liquidations were summed. Unconstrained numeric keeps the sums
    // exactly. A minute that was heard in full and had no liquidation has no row; one that was not heard in full has a row that is
    // not valid, with what was heard of it, down to nothing. Daily chunks, each compressed once it is 7 days old; a segment per
    // instrument, so a read of one instrument unpacks only its own.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE liquidations (
                instrument_id uuid NOT NULL,
                time timestamp with time zone NOT NULL,
                long_volume numeric NOT NULL,
                long_turnover numeric NOT NULL,
                long_count integer NOT NULL,
                short_volume numeric NOT NULL,
                short_turnover numeric NOT NULL,
                short_count integer NOT NULL,
                is_valid boolean NOT NULL,
                CONSTRAINT liquidations_pk PRIMARY KEY (instrument_id, time),
                CONSTRAINT liquidations_instrument_id_fk FOREIGN KEY (instrument_id) REFERENCES instruments (id)
            );

            SELECT create_hypertable('liquidations', by_range('time', INTERVAL '1 day'));

            ALTER TABLE liquidations SET (
                timescaledb.compress,
                timescaledb.compress_segmentby = 'instrument_id',
                timescaledb.compress_orderby = 'time'
            );

            SELECT add_compression_policy('liquidations', INTERVAL '7 days');
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE liquidations;");
    }
}