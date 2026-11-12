using System.Data;

using Dapper;

using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Queries;

internal static class InstrumentRepositoryQueries
{
    private const string SelectSql = """
                                     SELECT id, category, symbol, base_asset AS "BaseAsset", quote_asset AS "QuoteAsset", status,
                                            tick_size AS "TickSize", quantity_step AS "QuantityStep", min_quantity AS "MinQuantity",
                                            min_amount AS "MinAmount", contract_type AS "ContractType", launch_time AS "LaunchTime",
                                            first_seen_at AS "FirstSeenAt", delisted_at AS "DelistedAt"
                                     FROM instruments
                                     """;

    // A stored instrument of the same id takes every value of the saved one.
    private const string UpsertSql = """
                                     INSERT INTO instruments (id, category, symbol, base_asset, quote_asset, status, tick_size, quantity_step, min_quantity,
                                                              min_amount, contract_type, launch_time, first_seen_at, delisted_at)
                                     VALUES (@Id, @Category, @Symbol, @BaseAsset, @QuoteAsset, @Status, @TickSize, @QuantityStep, @MinQuantity,
                                             @MinAmount, @ContractType, @LaunchTime, @FirstSeenAt, @DelistedAt)
                                     ON CONFLICT (id) DO UPDATE SET
                                         category = EXCLUDED.category,
                                         symbol = EXCLUDED.symbol,
                                         base_asset = EXCLUDED.base_asset,
                                         quote_asset = EXCLUDED.quote_asset,
                                         status = EXCLUDED.status,
                                         tick_size = EXCLUDED.tick_size,
                                         quantity_step = EXCLUDED.quantity_step,
                                         min_quantity = EXCLUDED.min_quantity,
                                         min_amount = EXCLUDED.min_amount,
                                         contract_type = EXCLUDED.contract_type,
                                         launch_time = EXCLUDED.launch_time,
                                         first_seen_at = EXCLUDED.first_seen_at,
                                         delisted_at = EXCLUDED.delisted_at
                                     """;

    // The id breaks ties so the order is stable.
    public static CommandDefinition All(IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"{SelectSql} ORDER BY category, symbol, id", transaction: transaction, cancellationToken: cancellationToken);
    }

    // Dapper runs the command once per row, all in the transaction.
    public static CommandDefinition Upsert(IReadOnlyCollection<InstrumentDb> instruments, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UpsertSql, instruments, transaction, cancellationToken: cancellationToken);
    }
}