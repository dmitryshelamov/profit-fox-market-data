using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611150002)]
public sealed class CreateFundingPaymentSeriesTableMigration : Migration
{
    // One series per instrument: the stretch of time whose funding payments are all stored, its end outside it.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE funding_payment_series (
                instrument_id uuid NOT NULL,
                covered_from timestamp with time zone NOT NULL,
                covered_until timestamp with time zone NOT NULL,
                CONSTRAINT funding_payment_series_pk PRIMARY KEY (instrument_id),
                CONSTRAINT funding_payment_series_instrument_id_fk FOREIGN KEY (instrument_id) REFERENCES instruments (id)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE funding_payment_series;");
    }
}