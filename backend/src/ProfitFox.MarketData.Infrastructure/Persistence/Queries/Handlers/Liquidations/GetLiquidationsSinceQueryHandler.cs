using System.Runtime.CompilerServices;

using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Application.Queries.Liquidations;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Liquidations.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Liquidations.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Liquidations;

/// <summary>
///     Reads the stored minutes of liquidations row by row as they are asked for, so the caller passes them on without holding them all.
/// </summary>
internal sealed class GetLiquidationsSinceQueryHandler(NpgsqlConnection connection)
    : IStreamRequestHandler<GetLiquidationsSinceQuery, InstrumentLiquidationMinuteInternal>
{
    public async IAsyncEnumerable<InstrumentLiquidationMinuteInternal> Handle(
        GetLiquidationsSinceQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<InstrumentLiquidationMinuteDb> rows = connection.QueryUnbufferedAsync<InstrumentLiquidationMinuteDb>(
            GetLiquidationsSinceQueries.SinceSql,
            GetLiquidationsSinceQueries.Since(request.Since));

        await foreach (InstrumentLiquidationMinuteDb row in rows.WithCancellation(cancellationToken))
        {
            yield return new InstrumentLiquidationMinuteInternal(
                row.InstrumentId,
                row.Time,
                new LiquidationSumInternal(row.LongVolume, row.LongTurnover, row.LongCount),
                new LiquidationSumInternal(row.ShortVolume, row.ShortTurnover, row.ShortCount),
                row.IsValid);
        }
    }
}