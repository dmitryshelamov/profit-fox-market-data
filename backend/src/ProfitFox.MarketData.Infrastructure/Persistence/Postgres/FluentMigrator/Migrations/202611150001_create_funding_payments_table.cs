using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611150001)]
public sealed class CreateFundingPaymentsTableMigration : Migration
{
    // The funding payments of every instrument that pays funding, as its exchange settled them: unconstrained numeric keeps every rate
    // exactly. A plain table: a contract pays at most once an hour, so a year of the whole market is a few million rows. The index by
    // time is for the feed, which reads the payments of every instrument since a time; the key serves the reads of one instrument.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE funding_payments (
                instrument_id uuid NOT NULL,
                time timestamp with time zone NOT NULL,
                rate numeric NOT NULL,
                CONSTRAINT funding_payments_pk PRIMARY KEY (instrument_id, time),
                CONSTRAINT funding_payments_instrument_id_fk FOREIGN KEY (instrument_id) REFERENCES instruments (id)
            );

            CREATE INDEX funding_payments_time_instrument_id_index ON funding_payments (time, instrument_id);
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE funding_payments;");
    }
}