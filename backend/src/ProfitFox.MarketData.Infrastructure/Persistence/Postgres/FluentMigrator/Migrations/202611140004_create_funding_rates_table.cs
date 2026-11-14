using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611140004)]
public sealed class CreateFundingRatesTableMigration : Migration
{
    // The funding rate of the coming payment of every instrument that pays funding, a fraction, a point for each time it was collected:
    // unconstrained numeric keeps every rate exactly, and a time that was not collected has no row. Daily chunks, each compressed once
    // it is 7 days old; a segment per instrument, so a read of one instrument unpacks only its own.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE funding_rates (
                instrument_id uuid NOT NULL,
                time timestamp with time zone NOT NULL,
                rate numeric NOT NULL,
                CONSTRAINT funding_rates_pk PRIMARY KEY (instrument_id, time),
                CONSTRAINT funding_rates_instrument_id_fk FOREIGN KEY (instrument_id) REFERENCES instruments (id)
            );

            SELECT create_hypertable('funding_rates', by_range('time', INTERVAL '1 day'));

            ALTER TABLE funding_rates SET (
                timescaledb.compress,
                timescaledb.compress_segmentby = 'instrument_id',
                timescaledb.compress_orderby = 'time'
            );

            SELECT add_compression_policy('funding_rates', INTERVAL '7 days');
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE funding_rates;");
    }
}