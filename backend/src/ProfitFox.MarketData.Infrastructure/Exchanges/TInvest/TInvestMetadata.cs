using Grpc.Core;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

internal static class TInvestMetadata
{
    /// <summary>
    ///     The headers of a call to the Invest API: the token of the settings and the name of the app.
    /// </summary>
    /// <exception cref="NotSupportedException">TInvestApi:Token is not set, so no call can go; <paramref name="what" /> names what is not done.</exception>
    internal static Metadata Of(TInvestApiSettings settings, string what)
    {
        if (!settings.HasToken)
        {
            throw new NotSupportedException($"{what}: TInvestApi:Token is not set.");
        }

        return new Metadata { { "authorization", $"Bearer {settings.Token}" }, { "x-app-name", settings.AppName } };
    }
}