using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611150003)]
public sealed class CreateOpenInterestSeriesTableMigration : Migration
{
    // One series per instrument: the stretch of time whose points of open interest are all stored, its end outside it.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE open_interest_series (
                instrument_id uuid NOT NULL,
                covered_from timestamp with time zone NOT NULL,
                covered_until timestamp with time zone NOT NULL,
                CONSTRAINT open_interest_series_pk PRIMARY KEY (instrument_id),
                CONSTRAINT open_interest_series_instrument_id_fk FOREIGN KEY (instrument_id) REFERENCES instruments (id)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE open_interest_series;");
    }
}