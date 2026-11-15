using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611120003)]
public sealed class CreateTInvestSharesTableMigration : Migration
{
    // One listing per share of T-Invest. The uid identifies a share and stays when its ticker changes; a share has no minimum value of an
    // order.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE tinvest_shares (
                instrument_id uuid NOT NULL,
                uid text NOT NULL,
                ticker text NOT NULL,
                tick_size numeric NOT NULL,
                quantity_step numeric NOT NULL,
                min_quantity numeric NOT NULL,
                CONSTRAINT tinvest_shares_pk PRIMARY KEY (instrument_id),
                CONSTRAINT tinvest_shares_instrument_id_fk FOREIGN KEY (instrument_id) REFERENCES instruments (id),
                CONSTRAINT tinvest_shares_uid_unique UNIQUE (uid)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE tinvest_shares;");
    }
}