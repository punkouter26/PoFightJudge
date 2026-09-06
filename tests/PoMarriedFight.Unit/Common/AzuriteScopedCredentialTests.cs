using Azure.Core;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using PoMarriedFight.Api.Common;

namespace PoMarriedFight.Unit.Common;

public class AzuriteScopedCredentialTests
{
    [Fact]
    public async Task Every_token_request_is_rewritten_to_the_storage_scope_and_keeps_its_context()
    {
        var inner = Substitute.For<TokenCredential>();
        TokenRequestContext? seen = null;
        inner.GetTokenAsync(Arg.Do<TokenRequestContext>(c => seen = c), Arg.Any<CancellationToken>())
            .Returns(new AccessToken("t", DateTimeOffset.MaxValue));
        var sut = new AzuriteStorageScopedCredential(inner);

        // What Azure.Data.Tables actually asks for: the Cosmos Table resource, which Azurite rejects.
        var cosmos = new TokenRequestContext(["https://a232010e-820c-4083-83bb-3ace5fc29d0b/.default"], parentRequestId: "req-1", tenantId: "tenant-1");
        var token = await sut.GetTokenAsync(cosmos, CancellationToken.None);

        token.Token.Should().Be("t");
        seen!.Value.Scopes.Should().Equal(AzuriteStorageScopedCredential.StorageScope);
        seen.Value.ParentRequestId.Should().Be("req-1");
        seen.Value.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public void Sync_path_rewrites_too()
    {
        var inner = Substitute.For<TokenCredential>();
        inner.GetToken(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>()).Returns(new AccessToken("s", DateTimeOffset.MaxValue));
        var sut = new AzuriteStorageScopedCredential(inner);

        sut.GetToken(new TokenRequestContext(["https://storage.azure.com/.default"]), CancellationToken.None).Token.Should().Be("s");
        inner.Received(1).GetToken(Arg.Is<TokenRequestContext>(c => c.Scopes.Single() == AzuriteStorageScopedCredential.StorageScope), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Missing_endpoints_become_an_unreachable_placeholder_not_an_exception()
    {
        var empty = new ConfigurationBuilder().Build();

        StorageServiceExtensions.EndpointOrPlaceholder(empty, "x", "table").Host.Should().Be("unconfigured.table.core.windows.net");

        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection([new("x", "https://localhost:12002/devstoreaccount1")]).Build();
        StorageServiceExtensions.EndpointOrPlaceholder(configured, "x", "table").Should().Be(new Uri("https://localhost:12002/devstoreaccount1"));
    }

    [Fact]
    public void Only_the_local_dev_certificate_on_a_loopback_host_bypasses_chain_and_name_validation()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        using var localhost = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=localhost", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(100));
        using var other = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=evil.example", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(100));
        const System.Net.Security.SslPolicyErrors chain = System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors;
        const System.Net.Security.SslPolicyErrors nameMismatch = System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch;

        StorageServiceExtensions.IsLocalDevCertificate("localhost", localhost, chain).Should().BeTrue();
        StorageServiceExtensions.IsLocalDevCertificate("127.0.0.1", localhost, chain).Should().BeTrue();
        StorageServiceExtensions.IsLocalDevCertificate("127.0.0.1", localhost, chain | nameMismatch).Should().BeTrue("the dev cert only names localhost, and the endpoints use the IP");
        StorageServiceExtensions.IsLocalDevCertificate("storage.azure.com", localhost, chain).Should().BeFalse("only loopback hosts");
        StorageServiceExtensions.IsLocalDevCertificate("storage.azure.com", localhost, nameMismatch).Should().BeFalse("only loopback hosts");
        StorageServiceExtensions.IsLocalDevCertificate("localhost", other, chain).Should().BeFalse("only the dev cert subject");
        StorageServiceExtensions.IsLocalDevCertificate("localhost", localhost, System.Net.Security.SslPolicyErrors.RemoteCertificateNotAvailable).Should().BeFalse("only chain and name errors are forgiven");
        StorageServiceExtensions.IsLocalDevCertificate("anything", other, System.Net.Security.SslPolicyErrors.None).Should().BeTrue("a valid chain is always fine");
    }
}
