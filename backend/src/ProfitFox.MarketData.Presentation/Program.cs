using System.Text.Json.Serialization;

using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Extensions;
using ProfitFox.MarketData.Presentation.ExceptionHandlers;
using ProfitFox.MarketData.Presentation.GrpcServices;
using ProfitFox.MarketData.Presentation.Settings;

using Serilog;

namespace ProfitFox.MarketData.Presentation;

public class Program
{
    public static int Main(string[] args)
    {
        // Bootstrap logger catches startup errors before configuration is loaded.
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.Services.AddSerilog((services, configuration) => configuration
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services));

            builder.Services.AddInfrastructure(builder.Configuration);

            // An app with a broken CandleRead section stops at start.
            builder.Services.AddOptions<CandleReadSettings>()
                .Bind(builder.Configuration.GetSection(CandleReadSettings.Section))
                .Validate(read => read.MaxExchangeRequests > 0, "CandleRead:MaxExchangeRequests must be above zero.")
                .ValidateOnStart();

            // An app with a broken CandleFeed section stops at start.
            builder.Services.AddOptions<CandleFeedSettings>()
                .Bind(builder.Configuration.GetSection(CandleFeedSettings.Section))
                .Validate(feed => feed.MaxCatchUpHours > 0, "CandleFeed:MaxCatchUpHours must be above zero.")
                .ValidateOnStart();

            // The feed of closed candles; it needs HTTP/2, which has an endpoint of its own in the Kestrel section of the settings.
            builder.Services.AddGrpc();

            // The failures the application tells by an exception are answered in one place.
            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

            builder.Services
                .AddControllers()
                .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            WebApplication app = builder.Build();

            app.Services.MigrateToLatestVersion();

            app.UseSerilogRequestLogging();
            app.UseExceptionHandler();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "ProfitFox.MarketData v1"));
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();
            app.MapGrpcService<CandleFeedService>();

            app.Run();

            return 0;
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Application terminated unexpectedly");

            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}