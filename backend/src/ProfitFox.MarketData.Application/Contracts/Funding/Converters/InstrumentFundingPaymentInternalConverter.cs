using ProfitFox.MarketData.Domain.ValueObjects.Funding;

namespace ProfitFox.MarketData.Application.Contracts.Funding.Converters;

public static class InstrumentFundingPaymentInternalConverter
{
    /// <summary>
    ///     A payment knows no instrument, as its series holds it: the feed tells whose it is.
    /// </summary>
    public static InstrumentFundingPaymentInternal ToInternal(this FundingPayment payment, Guid instrumentId)
    {
        return new InstrumentFundingPaymentInternal(instrumentId, payment.Time, payment.Rate);
    }
}