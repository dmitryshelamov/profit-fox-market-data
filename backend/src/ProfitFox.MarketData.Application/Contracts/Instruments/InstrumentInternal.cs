using ProfitFox.MarketData.Application.Contracts.Instruments.Base;

namespace ProfitFox.MarketData.Application.Contracts.Instruments;

public sealed record InstrumentInternal(
    Guid Id,
    ListedInstrumentInternal Listing,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? DelistedAt);