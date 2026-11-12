using System.Data;

using Dapper;

using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Queries;

/// <summary>
///     An instrument is a row of <c>instruments</c> and the row of its listing in the table of its exchange; a read joins the two and splits
///     them on <see cref="SplitOn" />.
/// </summary>
internal static class InstrumentRepositoryQueries
{
    public const string SplitOn = "InstrumentId";

    private const string InstrumentColumns = """i.id, i.first_seen_at AS "FirstSeenAt", i.delisted_at AS "DelistedAt" """;

    // The id breaks ties so the order is stable.
    private const string BybitSql = $"""
                                     SELECT {InstrumentColumns},
                                            l.instrument_id AS "InstrumentId", l.category, l.symbol, l.base_asset AS "BaseAsset",
                                            l.quote_asset AS "QuoteAsset", l.status, l.tick_size AS "TickSize", l.quantity_step AS "QuantityStep",
                                            l.min_quantity AS "MinQuantity", l.min_amount AS "MinAmount", l.contract_type AS "ContractType",
                                            l.launch_time AS "LaunchTime"
                                     FROM instruments i
                                     JOIN bybit_listings l ON l.instrument_id = i.id
                                     ORDER BY l.category, l.symbol, i.id
                                     """;

    private const string TInvestSql = $"""
                                       SELECT {InstrumentColumns},
                                              s.instrument_id AS "InstrumentId", s.uid, s.ticker, s.tick_size AS "TickSize",
                                              s.quantity_step AS "QuantityStep", s.min_quantity AS "MinQuantity"
                                       FROM instruments i
                                       JOIN tinvest_shares s ON s.instrument_id = i.id
                                       ORDER BY s.ticker, i.id
                                       """;

    // A stored row of the same id takes every value of the saved one.
    private const string UpsertInstrumentsSql = """
                                                INSERT INTO instruments (id, first_seen_at, delisted_at)
                                                VALUES (@Id, @FirstSeenAt, @DelistedAt)
                                                ON CONFLICT (id) DO UPDATE SET
                                                    first_seen_at = EXCLUDED.first_seen_at,
                                                    delisted_at = EXCLUDED.delisted_at
                                                """;

    private const string UpsertBybitSql = """
                                          INSERT INTO bybit_listings (instrument_id, category, symbol, base_asset, quote_asset, status, tick_size,
                                                                      quantity_step, min_quantity, min_amount, contract_type, launch_time)
                                          VALUES (@InstrumentId, @Category, @Symbol, @BaseAsset, @QuoteAsset, @Status, @TickSize,
                                                  @QuantityStep, @MinQuantity, @MinAmount, @ContractType, @LaunchTime)
                                          ON CONFLICT (instrument_id) DO UPDATE SET
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
                                              launch_time = EXCLUDED.launch_time
                                          """;

    private const string UpsertTInvestSql = """
                                            INSERT INTO tinvest_shares (instrument_id, uid, ticker, tick_size, quantity_step, min_quantity)
                                            VALUES (@InstrumentId, @Uid, @Ticker, @TickSize, @QuantityStep, @MinQuantity)
                                            ON CONFLICT (instrument_id) DO UPDATE SET
                                                uid = EXCLUDED.uid,
                                                ticker = EXCLUDED.ticker,
                                                tick_size = EXCLUDED.tick_size,
                                                quantity_step = EXCLUDED.quantity_step,
                                                min_quantity = EXCLUDED.min_quantity
                                            """;

    public static CommandDefinition AllBybit(IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(BybitSql, transaction: transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition AllTInvest(IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(TInvestSql, transaction: transaction, cancellationToken: cancellationToken);
    }

    // Dapper runs each upsert once per row, all in the transaction.
    public static CommandDefinition UpsertInstruments(IReadOnlyCollection<InstrumentDb> rows, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UpsertInstrumentsSql, rows, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition UpsertBybit(IReadOnlyCollection<BybitListingDb> rows, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UpsertBybitSql, rows, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition UpsertTInvest(IReadOnlyCollection<TInvestShareDb> rows, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UpsertTInvestSql, rows, transaction, cancellationToken: cancellationToken);
    }
}