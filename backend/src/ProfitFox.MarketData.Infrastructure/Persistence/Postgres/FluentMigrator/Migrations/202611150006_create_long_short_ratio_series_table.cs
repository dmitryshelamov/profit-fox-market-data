using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611150006)]
public sealed class CreateLongShortRatioSeriesTableMigration : Migration
{
    // One series per instrument: the stretch of time whose points of the long/short ratio are all stored, its end outside it.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE long_short_ratio_series (
                instrument_id uuid NOT NULL,
                covered_from timestamp with time zone NOT NULL,
                covered_until timestamp with time zone NOT NULL,
                CONSTRAINT long_short_ratio_series_pk PRIMARY KEY (instrument_id),
                CONSTRAINT long_short_ratio_series_instrument_id_fk FOREIGN KEY (instrument_id) REFERENCES instruments (id)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE long_short_ratio_series;");
    }
}