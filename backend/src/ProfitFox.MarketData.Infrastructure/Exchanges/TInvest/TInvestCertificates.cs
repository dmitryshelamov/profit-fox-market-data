using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The trust of the channel to T-Invest. The server certificate of the Invest API is issued by the Russian Trusted Sub CA of the
///     Ministry of Digital Development, whose root the stores of macOS and of the Linux images do not hold; the channel trusts that root
///     alone, from <c>Exchanges/TInvest/Certificates/russian_trusted_root_ca.cer</c> next to the app, and nothing else on the host
///     starts trusting it. The server sends its Sub CA in the handshake, so a new Sub CA of the same root needs no change.
/// </summary>
internal static class TInvestCertificates
{
    private static readonly string RootFile = Path.Combine("Exchanges", "TInvest", "Certificates", "russian_trusted_root_ca.cer");

    // The extended key usage of a server certificate, id-kp-serverAuth.
    private static readonly Oid ServerAuthentication = new Oid("1.3.6.1.5.5.7.3.1");

    /// <summary>
    ///     The policy the channel checks the chain of the server by: up to the root of the file and no other, for server authentication,
    ///     with no certificate downloaded. Revocation is not checked, as by default for a client.
    /// </summary>
    public static X509ChainPolicy ChainPolicy()
    {
        X509Certificate2 root = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(AppContext.BaseDirectory, RootFile));

        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            DisableCertificateDownloads = true,
            RevocationMode = X509RevocationMode.NoCheck
        };
        policy.CustomTrustStore.Add(root);
        policy.ApplicationPolicy.Add(ServerAuthentication);

        return policy;
    }
}