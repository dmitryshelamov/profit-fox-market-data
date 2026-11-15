using MediatR;

namespace ProfitFox.MarketData.Application.Commands.OpenInterest.LoadOpenInterest;

/// <summary>
///     Loads one page of the open interest of the instrument its series does not cover between <see cref="From" /> and
///     <see cref="RequestedAt" />, saved at once. The time since the coverage ended comes first, the oldest page of it first: when the
///     collection kept every point of that page, it is covered with no request. Then comes the history, the newest page of it first.
///     A caller sends it again while it gives <see cref="LoadOpenInterestResult.MoreLeft" />.
/// </summary>
public sealed record LoadOpenInterestCommand(Guid InstrumentId, DateTimeOffset From, DateTimeOffset RequestedAt) : IRequest<LoadOpenInterestResult>;