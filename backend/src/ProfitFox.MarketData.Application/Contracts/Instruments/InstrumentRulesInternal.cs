namespace ProfitFox.MarketData.Application.Contracts.Instruments;

public sealed record InstrumentRulesInternal(
    decimal TickSize,
    decimal QuantityStep,
    decimal MinQuantity,
    decimal? MinAmount);