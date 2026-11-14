using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611140002)]
public sealed class CreateCandleSeriesTableMigration : Migration
{
    // One series per instrument: the ranges its candles cover, as one multirange, which keeps them apart and oldest first.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE candle_series (
                instrument_id uuid NOT NULL,
                covered tstzmultirange NOT NULL,
                CONSTRAINT candle_series_pk PRIMARY KEY (instrument_id),
                CONSTRAINT candle_series_instrument_id_fk FOREIGN KEY (instrument_id) REFERENCES instruments (id)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE candle_series;");
    }
}