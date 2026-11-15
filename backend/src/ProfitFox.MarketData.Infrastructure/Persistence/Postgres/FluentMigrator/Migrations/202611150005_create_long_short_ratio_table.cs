using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611150005)]
public sealed class CreateLongShortRatioTableMigration : Migration
{
    // The long/short ratio of every instrument that has one, a point for each step it was loaded for: the shares of the accounts
    // holding the instrument that were net long and net short, fractions. Unconstrained numeric keeps every share exactly, and a step
    // not loaded has no row. Daily chunks, each compressed once it is 7 days old; a segment per instrument, so a read of one instrument
    // unpacks only its own.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE long_short_ratio (
                instrument_id uuid NOT NULL,
                time timestamp with time zone NOT NULL,
                long numeric NOT NULL,
                short numeric NOT NULL,
                CONSTRAINT long_short_ratio_pk PRIMARY KEY (instrument_id, time),
                CONSTRAINT long_short_ratio_instrument_id_fk FOREIGN KEY (instrument_id) REFERENCES instruments (id)
            );

            SELECT create_hypertable('long_short_ratio', by_range('time', INTERVAL '1 day'));

            ALTER TABLE long_short_ratio SET (
                timescaledb.compress,
                timescaledb.compress_segmentby = 'instrument_id',
                timescaledb.compress_orderby = 'time'
            );

            SELECT add_compression_policy('long_short_ratio', INTERVAL '7 days');
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE long_short_ratio;");
    }
}