using System.Runtime.CompilerServices;

using MediatR;

using Microsoft.Extensions.Options;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles;

/// <summary>
///     Reads the candles of the history a part of the window a statement, <see cref="HistoryPeriod" />, row by row as
///     <see cref="HistoryRows" /> reads them: Postgres sorts one part of the window, and the caller passes the rows on without holding them.
///     The minutes come as stored; the candles of a longer interval are built from them.
/// </summary>
internal sealed class GetCandleHistoryQueryHandler(NpgsqlConnection connection, IOptionsMonitor<HistoryReadSettings> settings) : IStreamRequestHandler<GetCandleHistoryQuery, ClosedCandleInternal>
{
    public async IAsyncEnumerable<ClosedCandleInternal> Handle(GetCandleHistoryQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Read once, so a change of the settings applies from the next call and not in the middle of this one.
        int timeoutSeconds = settings.CurrentValue.StatementTimeoutSeconds;
        await HistoryConnection.PrepareAsync(connection, timeoutSeconds, cancellationToken);

        string sql = request.Interval == CandleInterval.OneMinute ? GetCandleHistoryQueries.MinutesSql : GetCandleHistoryQueries.BucketsSql;

        foreach (HistoryPeriod period in HistoryPeriod.Split(request.Coverage))
        {
            IAsyncEnumerable<ClosedCandleDb> rows =
                HistoryRows.ReadAsync<ClosedCandleDb>(connection, sql, GetCandleHistoryQueries.Period(period, request.Interval), timeoutSeconds, cancellationToken);

            await foreach (ClosedCandleDb row in rows)
            {
                yield return new ClosedCandleInternal(
                    row.Exchange,
                    row.InstrumentId,
                    row.OpenTime,
                    row.Open,
                    row.High,
                    row.Low,
                    row.Close,
                    row.Volume,
                    row.Turnover,
                    row.BuyVolume);
            }
        }
    }
}