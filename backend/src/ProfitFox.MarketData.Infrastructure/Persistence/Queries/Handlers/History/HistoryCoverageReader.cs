using Dapper;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

/// <summary>
///     Reads the coverage of a request of history, whatever its kind of data: the statement of a kind tells only where its coverage is.
/// </summary>
internal static class HistoryCoverageReader
{
    /// <summary>
    ///     The coverage <paramref name="coverageSql" /> reads, of every instrument the request reads, ordered by id; each statement waits
    ///     for storage <paramref name="timeoutSeconds" /> at most.
    /// </summary>
    /// <exception cref="InstrumentNotFoundException">An instrument named is not in the catalog.</exception>
    /// <exception cref="StorageTimeoutException">Storage did not answer a statement in time.</exception>
    public static async Task<IReadOnlyList<InstrumentCoverageInternal>> ReadAsync(
        NpgsqlConnection connection,
        string coverageSql,
        IReadOnlyCollection<Guid> instrumentIds,
        IReadOnlyCollection<Exchange> exchanges,
        HistoryWindow window,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = [.. instrumentIds];
        IEnumerable<InstrumentRangeDb> rows;

        try
        {
            if (ids.Length > 0)
            {
                Guid? unknown = await connection.QueryFirstOrDefaultAsync<Guid?>(
                    new CommandDefinition(HistoryQueries.UnknownSql, new { InstrumentIds = ids }, commandTimeout: timeoutSeconds, cancellationToken: cancellationToken));

                if (unknown is not null)
                {
                    throw new InstrumentNotFoundException(unknown.Value);
                }
            }

            rows = await connection.QueryAsync<InstrumentRangeDb>(
                new CommandDefinition(
                    coverageSql,
                    new
                    {
                        InstrumentIds = ids,
                        Exchanges = exchanges.Select(exchange => exchange.ToString()).ToArray(),
                        From = window.From.ToUniversalTime(),
                        To = window.To.ToUniversalTime()
                    },
                    commandTimeout: timeoutSeconds,
                    cancellationToken: cancellationToken));
        }
        catch (NpgsqlException exception) when (exception.InnerException is TimeoutException)
        {
            throw new StorageTimeoutException(timeoutSeconds, exception);
        }

        // The rows come ordered by instrument and then by range, and a group keeps the order of the rows it took.
        return
        [
            .. rows.GroupBy(row => row.InstrumentId)
                .Select(ranges => new InstrumentCoverageInternal(
                    ranges.Key,
                    [.. ranges.Where(range => range.From is not null).Select(range => new TimeRangeInternal(range.From!.Value, range.Until!.Value))]))
        ];
    }
}