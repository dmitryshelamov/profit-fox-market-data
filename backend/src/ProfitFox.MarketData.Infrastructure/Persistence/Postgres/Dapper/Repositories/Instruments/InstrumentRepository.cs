using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Converters;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments;

/// <summary>
///     Works on the connection of the scope, in the transaction of the command if one runs; Dapper opens a closed connection itself. An
///     instrument is stored in <c>instruments</c> and its listing in the table of its exchange.
/// </summary>
internal sealed class InstrumentRepository(
    NpgsqlConnection connection,
    PostgresTransactionAccessor transactions,
    PostgresTransactionExecutor transactionExecutor) : IInstrumentRepository
{
    // The instruments of Bybit by category and symbol, then the shares of T-Invest by ticker.
    public async Task<IReadOnlyList<Instrument>> GetAllAsync(CancellationToken cancellationToken)
    {
        IEnumerable<Instrument> bybit = await connection.QueryAsync<InstrumentDb, BybitListingDb, Instrument>(
            InstrumentRepositoryQueries.AllBybit(transactions.Current, cancellationToken),
            (instrument, listing) => InstrumentDbConverter.ToDomain(instrument, BybitListingDbConverter.ToDomain(listing)),
            InstrumentRepositoryQueries.SplitOn);

        IEnumerable<Instrument> tinvest = await connection.QueryAsync<InstrumentDb, TInvestShareDb, Instrument>(
            InstrumentRepositoryQueries.AllTInvest(transactions.Current, cancellationToken),
            (instrument, share) => InstrumentDbConverter.ToDomain(instrument, TInvestShareDbConverter.ToDomain(share)),
            InstrumentRepositoryQueries.SplitOn);

        return [.. bybit, .. tinvest];
    }

    // Saves all together even outside a command: without a transaction of its own it opens one. The rows of instruments go first, as the
    // listings refer to them.
    public async Task SaveAsync(IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken)
    {
        if (instruments.Count == 0)
        {
            return;
        }

        List<InstrumentDb> rows = [.. instruments.Select(InstrumentDbConverter.FromDomain)];
        var bybit = new List<BybitListingDb>();
        var tinvest = new List<TInvestShareDb>();

        foreach (Instrument instrument in instruments)
        {
            switch (instrument.Listing)
            {
                case BybitListedInstrument listing:
                    bybit.Add(BybitListingDbConverter.FromDomain(instrument.Id, listing));

                    break;
                case TInvestListedShare share:
                    tinvest.Add(TInvestShareDbConverter.FromDomain(instrument.Id, share));

                    break;
                default:
                    throw new NotSupportedException(
                        $"Instrument {instrument.Id} has a listing of {instrument.Listing.GetType().Name}, which the catalog does not store.");
            }
        }

        await transactionExecutor.ExecuteAsync(
            async () =>
            {
                await connection.ExecuteAsync(InstrumentRepositoryQueries.UpsertInstruments(rows, transactions.Current, cancellationToken));
                await connection.ExecuteAsync(InstrumentRepositoryQueries.UpsertBybit(bybit, transactions.Current, cancellationToken));
                await connection.ExecuteAsync(InstrumentRepositoryQueries.UpsertTInvest(tinvest, transactions.Current, cancellationToken));
            },
            cancellationToken);
    }
}