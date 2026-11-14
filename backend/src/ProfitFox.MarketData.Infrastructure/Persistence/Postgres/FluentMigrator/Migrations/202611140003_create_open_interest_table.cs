using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611140003)]
public sealed class CreateOpenInterestTableMigration : Migration
{
    // The open interest of every instrument that has one, in its base asset, a point for each time it was collected: unconstrained
    // numeric keeps every value exactly, and a time that was not collected has no row. Daily chunks, each compressed once it is 7 days
    // old; a segment per instrument, so a read of one instrument unpacks only its own.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE open_interest (
                instrument_id uuid NOT NULL,
                time timestamp with time zone NOT NULL,
                value numeric NOT NULL,
                CONSTRAINT open_interest_pk PRIMARY KEY (instrument_id, time),
                CONSTRAINT open_interest_instrument_id_fk FOREIGN KEY (instrument_id) REFERENCES instruments (id)
            );

            SELECT create_hypertable('open_interest', by_range('time', INTERVAL '1 day'));

            ALTER TABLE open_interest SET (
                timescaledb.compress,
                timescaledb.compress_segmentby = 'instrument_id',
                timescaledb.compress_orderby = 'time'
            );

            SELECT add_compression_policy('open_interest', INTERVAL '7 days');
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE open_interest;");
    }
}