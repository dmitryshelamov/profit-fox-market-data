using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Converters;

/// <summary>
///     A share of T-Invest has no minimum value of an order, so the table keeps none.
/// </summary>
internal static class TInvestShareDbConverter
{
    internal static TInvestShareDb FromDomain(Guid instrumentId, TInvestListedShare share)
    {
        return new TInvestShareDb
        {
            InstrumentId = instrumentId,
            Uid = share.Uid,
            Ticker = share.Ticker,
            TickSize = share.Rules.TickSize,
            QuantityStep = share.Rules.QuantityStep,
            MinQuantity = share.Rules.MinQuantity
        };
    }

    internal static TInvestListedShare ToDomain(TInvestShareDb row)
    {
        return new TInvestListedShare(row.Uid, row.Ticker, new InstrumentRules(row.TickSize, row.QuantityStep, row.MinQuantity));
    }
}