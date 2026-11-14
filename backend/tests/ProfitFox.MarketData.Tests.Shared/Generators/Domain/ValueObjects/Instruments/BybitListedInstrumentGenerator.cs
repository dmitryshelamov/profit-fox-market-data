using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

/// <summary>
///     Listings of Bybit that trade, quoted in USDT: <c>BTC</c> gives <c>BTCUSDT</c>. A linear contract is perpetual with a launch time and a
///     minimum value of an order; a spot pair has neither, so the two cover the filled and the empty optional values.
/// </summary>
public static class BybitListedInstrumentGenerator
{
    private const string QuoteAsset = "USDT";

    private static readonly DateTimeOffset LaunchTime = new DateTimeOffset(year: 2020, month: 3, day: 30, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    public static BybitListedInstrument GenerateLinear(string baseAsset = "BTC", decimal tickSize = 0.1m)
    {
        return new BybitListedInstrument(
            BybitCategory.Linear,
            baseAsset + QuoteAsset,
            baseAsset,
            QuoteAsset,
            BybitInstrumentStatus.Trading,
            new InstrumentRules(tickSize, quantityStep: 0.001m, minQuantity: 0.001m, minAmount: 5m),
            BybitContractType.LinearPerpetual,
            LaunchTime);
    }

    public static BybitListedInstrument GenerateSpot(string baseAsset = "BTC", decimal tickSize = 0.01m)
    {
        return new BybitListedInstrument(
            BybitCategory.Spot,
            baseAsset + QuoteAsset,
            baseAsset,
            QuoteAsset,
            BybitInstrumentStatus.Trading,
            new InstrumentRules(tickSize, quantityStep: 0.000001m, minQuantity: 0.000001m));
    }
}