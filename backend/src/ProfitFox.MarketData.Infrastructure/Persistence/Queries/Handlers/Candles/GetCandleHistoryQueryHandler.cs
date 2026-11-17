using System.Runtime.CompilerServices;

using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles;

/// <summary>
///     Reads the candles of the history a UTC day a statement, row by row as they are asked for: Postgres sorts one day of the window, and
///     the caller passes the rows on without holding them. The minutes come as stored; the candles of a longer interval are built from them.
/// </summary>
internal sealed class GetCandleHistoryQueryHandler(NpgsqlConnection connection) : IStreamRequestHandler<GetCandleHistoryQuery, ClosedCandleInternal>
{
    public async IAsyncEnumerable<ClosedCandleInternal> Handle(GetCandleHistoryQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await HistoryConnection.PrepareAsync(connection, cancellationToken);

        string sql = request.Interval == CandleInterval.OneMinute ? GetCandleHistoryQueries.MinutesSql : GetCandleHistoryQueries.BucketsSql;

        foreach (HistoryDay day in HistoryDay.Split(request.Coverage))
        {
            IAsyncEnumerable<ClosedCandleDb> rows = connection.QueryUnbufferedAsync<ClosedCandleDb>(sql, GetCandleHistoryQueries.Day(day, request.Interval));

            await foreach (ClosedCandleDb row in rows.WithCancellation(cancellationToken))
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