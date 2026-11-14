using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.TInvest;

/// <summary>
///     Checks the chain the way the channel does, on the certificates T-Invest presented on 2026-11-13, at that moment, so the test does not
///     age with them.
/// </summary>
[TestFixture]
public class TInvestCertificatesTests
{
    private const string Host = "invest-public-api.tbank.ru";

    private static readonly DateTime Presented = new DateTime(year: 2026, month: 11, day: 13, hour: 0, minute: 0, second: 0, DateTimeKind.Utc);

    private static X509Certificate2 Load(string file)
    {
        return X509CertificateLoader.LoadCertificateFromFile(Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Exchanges", "TInvest", "Certificates", file));
    }

    private static bool Builds(X509Certificate2 server, params X509Certificate2[] presented)
    {
        X509ChainPolicy policy = TInvestCertificates.ChainPolicy();
        policy.ExtraStore.AddRange(presented);
        policy.VerificationTime = Presented;

        using var chain = new X509Chain { ChainPolicy = policy };

        return chain.Build(server);
    }

    [Test]
    public void A_server_of_another_root_is_refused()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={Host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using X509Certificate2 selfSigned = request.CreateSelfSigned(new DateTimeOffset(Presented.AddDays(-1)), new DateTimeOffset(Presented.AddDays(1)));

        Assert.That(Builds(selfSigned), Is.False);
    }

    [Test]
    public void The_chain_of_tinvest_ends_at_the_trusted_root()
    {
        Assert.That(Builds(Load("tbank_server.cer"), Load("russian_trusted_sub_ca.cer")), Is.True);
    }
}