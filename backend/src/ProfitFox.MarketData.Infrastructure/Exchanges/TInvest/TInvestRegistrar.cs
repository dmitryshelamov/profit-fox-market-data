using System.Net.Security;

using Grpc.Net.Client;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Ports.Instruments;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

internal static class TInvestRegistrar
{
    /// <summary>
    ///     One gRPC channel to T-Invest for the process, opened on the first call and trusting the server by
    ///     <see cref="TInvestCertificates" />, and the client of its instruments over it; a test host
    ///     replaces the client to answer the calls itself.
    /// </summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        services.AddOptions<TInvestApiSettings>()
            .Bind(configuration.GetSection(TInvestApiSettings.Section))
            .Validate(
                tinvest => Uri.TryCreate(tinvest.Endpoint, UriKind.Absolute, out Uri? endpoint) && endpoint.Scheme == Uri.UriSchemeHttps,
                "TInvestApi:Endpoint must be an https address.")
            .ValidateOnStart();

        // The server is trusted by the root of TInvestCertificates. A call cancelled by its token is an OperationCanceledException, as
        // everywhere else, not an RpcException.
        services.AddSingleton(provider => GrpcChannel.ForAddress(
            provider.GetRequiredService<IOptions<TInvestApiSettings>>().Value.Endpoint,
            new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = TInvestCertificates.ChainPolicy() }
                },
                ThrowOperationCanceledOnCancellation = true
            }));
        services.AddSingleton(provider => new InstrumentsService.InstrumentsServiceClient(provider.GetRequiredService<GrpcChannel>()));
        services.AddScoped<ITInvestSharesClient, TInvestSharesClient>();
    }
}