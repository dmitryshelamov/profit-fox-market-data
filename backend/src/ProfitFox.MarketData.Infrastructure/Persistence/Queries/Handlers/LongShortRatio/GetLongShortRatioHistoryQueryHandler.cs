using System.Runtime.CompilerServices;

using MediatR;

using Microsoft.Extensions.Options;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Application.Queries.LongShortRatio;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio;

/// <summary>
///     Reads the points of the long/short ratio of the history a part of the window a statement, <see cref="HistoryPeriod" />, row by row
///     as <see cref="HistoryRows" /> reads them, so the caller passes them on without holding them all.
/// </summary>
internal sealed class GetLongShortRatioHistoryQueryHandler(NpgsqlConnection connection, IOptionsMonitor<HistoryReadSettings> settings) : IStreamRequestHandler<GetLongShortRatioHistoryQuery, InstrumentLongShortRatioInternal>
{
    public async IAsyncEnumerable<InstrumentLongShortRatioInternal> Handle(GetLongShortRatioHistoryQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Read once, so a change of the settings applies from the next call and not in the middle of this one.
        int timeoutSeconds = settings.CurrentValue.StatementTimeoutSeconds;
        await HistoryConnection.PrepareAsync(connection, timeoutSeconds, cancellationToken);

        foreach (HistoryPeriod period in HistoryPeriod.Split(request.Coverage))
        {
            IAsyncEnumerable<InstrumentLongShortRatioDb> rows = HistoryRows.ReadAsync<InstrumentLongShortRatioDb>(
                connection,
                GetLongShortRatioHistoryQueries.PointsSql,
                GetLongShortRatioHistoryQueries.Period(period),
                timeoutSeconds,
                cancellationToken);

            await foreach (InstrumentLongShortRatioDb row in rows)
            {
                yield return new InstrumentLongShortRatioInternal(row.InstrumentId, row.Time, row.Long, row.Short);
            }
        }
    }
}