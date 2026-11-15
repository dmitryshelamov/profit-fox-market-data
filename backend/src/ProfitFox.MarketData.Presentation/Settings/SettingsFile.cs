using Serilog;

namespace ProfitFox.MarketData.Presentation.Settings;

/// <summary>
///     The settings file on the server, <c>config/market-data.json</c> under the content root, which production mounts from
///     <c>~/profit-fox-market-data/config</c>: it overrides every other source, the environment included, and is read again on each
///     change, so a setting the app reads live, such as <c>BybitApi:LimitPauseSeconds</c>, changes without a restart. Without the file the
///     app runs on the settings of its image; a file that is not valid JSON is skipped with a warning, at start and on a change alike, so
///     a broken edit never keeps the app from starting.
/// </summary>
public static class SettingsFile
{
    public const string RelativePath = "config/market-data.json";

    public static void AddTo(IConfigurationBuilder configuration)
    {
        configuration.AddJsonFile(source =>
        {
            source.Path = RelativePath;
            source.Optional = true;
            source.ReloadOnChange = true;
            source.OnLoadException = context =>
            {
                Log.Warning(context.Exception, "{SettingsFile} is not valid JSON; the settings of the image hold until it is fixed.", RelativePath);
                context.Ignore = true;
            };
        });
    }
}