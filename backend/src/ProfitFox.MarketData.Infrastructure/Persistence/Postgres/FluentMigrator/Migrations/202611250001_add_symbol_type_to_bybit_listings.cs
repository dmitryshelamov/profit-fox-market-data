using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611250001)]
public sealed class AddSymbolTypeToBybitListingsMigration : Migration
{
    // What Bybit says an instrument is, by the name of a member of BybitSymbolType; NULL when Bybit gives none. Stored rows stay NULL until
    // the next sync of their category.
    public override void Up()
    {
        Execute.Sql("ALTER TABLE bybit_listings ADD COLUMN symbol_type text NULL;");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE bybit_listings DROP COLUMN symbol_type;");
    }
}