using System.Runtime.CompilerServices;

using MediatR;

using Microsoft.Extensions.Options;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Queries.OpenInterest;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest;

/// <summary>
///     Reads the points of open interest of the history a part of the window a statement, <see cref="HistoryPeriod" />, row by row as
///     <see cref="HistoryRows" /> reads them, so the caller passes them on without holding them all.
/// </summary>
internal sealed class GetOpenInterestHistoryQueryHandler(NpgsqlConnection connection, IOptionsMonitor<HistoryReadSettings> settings) : IStreamRequestHandler<GetOpenInterestHistoryQuery, InstrumentOpenInterestInternal>
{
    public async IAsyncEnumerable<InstrumentOpenInterestInternal> Handle(GetOpenInterestHistoryQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Read once, so a change of the settings applies from the next call and not in the middle of this one.
        int timeoutSeconds = settings.CurrentValue.StatementTimeoutSeconds;
        await HistoryConnection.PrepareAsync(connection, timeoutSeconds, cancellationToken);

        foreach (HistoryPeriod period in HistoryPeriod.Split(request.Coverage))
        {
            IAsyncEnumerable<InstrumentOpenInterestDb> rows = HistoryRows.ReadAsync<InstrumentOpenInterestDb>(
                connection,
                GetOpenInterestHistoryQueries.PointsSql,
                GetOpenInterestHistoryQueries.Period(period),
                timeoutSeconds,
                cancellationToken);

            await foreach (InstrumentOpenInterestDb row in rows)
            {
                yield return new InstrumentOpenInterestInternal(row.InstrumentId, row.Time, row.Value);
            }
        }
    }
}