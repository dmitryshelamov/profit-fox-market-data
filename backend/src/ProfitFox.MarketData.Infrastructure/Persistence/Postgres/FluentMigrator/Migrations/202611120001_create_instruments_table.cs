using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611120001)]
public sealed class CreateInstrumentsTableMigration : Migration
{
    // Enums are stored by the names of their members. A symbol is unique only within its category: BTCUSDT is both spot and linear.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE instruments (
                id uuid NOT NULL,
                category text NOT NULL,
                symbol text NOT NULL,
                base_asset text NOT NULL,
                quote_asset text NOT NULL,
                status text NOT NULL,
                tick_size numeric NOT NULL,
                quantity_step numeric NOT NULL,
                min_quantity numeric NOT NULL,
                min_amount numeric NULL,
                contract_type text NULL,
                launch_time timestamp with time zone NULL,
                first_seen_at timestamp with time zone NOT NULL,
                delisted_at timestamp with time zone NULL,
                CONSTRAINT instruments_pk PRIMARY KEY (id),
                CONSTRAINT instruments_category_symbol_unique UNIQUE (category, symbol)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE instruments;");
    }
}