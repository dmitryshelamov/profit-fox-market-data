using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611120001)]
public sealed class CreateInstrumentsTableMigration : Migration
{
    // What every instrument has, whatever its exchange; the listing of each exchange is in a table of its own.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE instruments (
                id uuid NOT NULL,
                first_seen_at timestamp with time zone NOT NULL,
                delisted_at timestamp with time zone NULL,
                CONSTRAINT instruments_pk PRIMARY KEY (id)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE instruments;");
    }
}