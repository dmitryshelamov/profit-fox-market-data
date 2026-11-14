using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611120002)]
public sealed class CreateBybitListingsTableMigration : Migration
{
    // One listing per instrument of Bybit. Enums are stored by the names of their members. A symbol is unique only within its category:
    // BTCUSDT is both spot and linear.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE bybit_listings (
                instrument_id uuid NOT NULL,
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
                CONSTRAINT bybit_listings_pk PRIMARY KEY (instrument_id),
                CONSTRAINT bybit_listings_instrument_id_fk FOREIGN KEY (instrument_id) REFERENCES instruments (id),
                CONSTRAINT bybit_listings_category_symbol_unique UNIQUE (category, symbol)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE bybit_listings;");
    }
}