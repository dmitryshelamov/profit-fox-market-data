using MediatR;

namespace ProfitFox.MarketData.Application.Commands.LongShortRatio.LoadLongShortRatio;

/// <summary>
///     Loads one page of the long/short ratio of the instrument its series does not cover between <see cref="From" /> and
///     <see cref="RequestedAt" />, saved at once. The time since the coverage ended comes first, the oldest page of it first, and its
///     points are told to the feed of the long/short ratio: they are the new ones. Then comes the history, the newest page of it
///     first, which is not told. A caller sends it again while it gives <see cref="LoadLongShortRatioResult.MoreLeft" />.
/// </summary>
public sealed record LoadLongShortRatioCommand(Guid InstrumentId, DateTimeOffset From, DateTimeOffset RequestedAt) : IRequest<LoadLongShortRatioResult>;