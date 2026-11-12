using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit.Converters;

/// <summary>
///     Turns the symbols of Bybit.Net into listings of the domain. A code the domain has no value for fails the whole list: a guessed
///     status or a skipped symbol would put a wrong instrument into the catalog or delist a listed one.
/// </summary>
internal static class BybitSymbolConverter
{
    /// <summary>
    ///     A spot pair: the quantity steps by the base precision, the minimum value of an order is in the quote coin.
    /// </summary>
    internal static BybitListedInstrument ToDomain(this BybitSpotSymbol spot)
    {
        return new BybitListedInstrument(
            BybitCategory.Spot,
            spot.Name,
            spot.BaseAsset,
            spot.QuoteAsset,
            Status(spot.Name, spot.Status),
            new InstrumentRules(
                spot.PriceFilter!.TickSize,
                spot.LotSizeFilter!.BasePrecision,
                spot.LotSizeFilter.MinOrderQuantity,
                spot.LotSizeFilter.MinOrderValue));
    }

    /// <summary>
    ///     A linear contract: the quantity steps by the quantity step, the minimum value of an order is its notional value.
    /// </summary>
    internal static BybitListedInstrument ToDomain(this BybitLinearInverseSymbol linear)
    {
        return new BybitListedInstrument(
            BybitCategory.Linear,
            linear.Name,
            linear.BaseAsset,
            linear.QuoteAsset,
            Status(linear.Name, linear.Status),
            new InstrumentRules(
                linear.PriceFilter!.TickSize,
                linear.LotSizeFilter!.QuantityStep,
                linear.LotSizeFilter.MinOrderQuantity,
                linear.LotSizeFilter.MinNotionalValue),
            LinearContractType(linear.Name, linear.ContractType),
            new DateTimeOffset(DateTime.SpecifyKind(linear.LaunchTime, DateTimeKind.Utc)));
    }

    private static InstrumentStatus Status(string symbol, SymbolStatus status)
    {
        return status switch
        {
            SymbolStatus.PreLaunch => InstrumentStatus.PreLaunch,
            SymbolStatus.PendingOpen => InstrumentStatus.PendingOpen,
            SymbolStatus.Trading => InstrumentStatus.Trading,
            SymbolStatus.Delivering => InstrumentStatus.Delivering,
            SymbolStatus.Closed => InstrumentStatus.Closed,
            _ => throw new InvalidOperationException($"Bybit lists {symbol} with status {status}, which the catalog does not know.")
        };
    }

    private static BybitContractType LinearContractType(string symbol, ContractTypeV5? type)
    {
        return type switch
        {
            ContractTypeV5.LinearPerpetual => BybitContractType.LinearPerpetual,
            ContractTypeV5.LinearFutures => BybitContractType.LinearFutures,
            _ => throw new InvalidOperationException($"Bybit lists linear {symbol} with contract type {type}, which is not linear.")
        };
    }
}