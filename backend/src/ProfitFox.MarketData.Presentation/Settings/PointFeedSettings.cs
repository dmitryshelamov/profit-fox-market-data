namespace ProfitFox.MarketData.Presentation.Settings;

/// <summary>
///     The settings of a feed of points, a section a feed, named after the feed: <c>OpenInterestFeed</c>, <c>FundingRateFeed</c>,
///     <c>FundingPaymentFeed</c>, <c>LiquidationFeed</c>. How far back a subscriber of the feed may catch up from storage.
/// </summary>
public sealed class PointFeedSettings
{
    public const string OpenInterestFeed = "OpenInterestFeed";

    public const string FundingRateFeed = "FundingRateFeed";

    public const string FundingPaymentFeed = "FundingPaymentFeed";

    public const string LiquidationFeed = "LiquidationFeed";

    /// <summary>
    ///     The sections of the feeds, each bound and checked by its name.
    /// </summary>
    public static readonly IReadOnlyList<string> Sections = [OpenInterestFeed, FundingRateFeed, FundingPaymentFeed, LiquidationFeed];

    /// <summary>
    ///     The most hours back from now a subscription may start from: a catch-up is of the whole market at once, and older data is
    ///     asked for an instrument at a time.
    /// </summary>
    public int MaxCatchUpHours { get; init; } = 24;
}