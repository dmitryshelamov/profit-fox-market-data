namespace ProfitFox.MarketData.Application.Exceptions;

/// <summary>
///     A subscriber of a feed read slower than the feed told, so what waited for it no longer fitted and it was cut off. It subscribes
///     again and reads what it missed from storage.
/// </summary>
public sealed class SubscriberFellBehindException() : Exception("The subscriber read slower than the feed told and was cut off.");