namespace ProfitFox.MarketData.Application.Contracts.Instruments;

public sealed record InstrumentInternal(
    Guid Id,
    BybitListedInstrumentInternal Listing,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? DelistedAt);