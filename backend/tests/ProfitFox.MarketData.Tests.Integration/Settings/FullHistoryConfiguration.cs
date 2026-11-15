namespace ProfitFox.MarketData.Tests.Integration.Settings;

/// <summary>
///     The <c>FullHistory</c> section of the settings as configuration holds it, for a test that starts a loading with the app's binding.
/// </summary>
internal static class FullHistoryConfiguration
{
    /// <summary>
    ///     The section naming <paramref name="symbols" /> of Bybit; none names no instrument.
    /// </summary>
    public static Dictionary<string, string?> Of(IEnumerable<string> symbols)
    {
        return symbols
            .Select((symbol, index) => KeyValuePair.Create($"FullHistory:Bybit:Symbols:{index}", (string?)symbol))
            .ToDictionary();
    }
}