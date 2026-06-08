using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Services.Oidc;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Provider-agnostic behavior of the OIDC HTTP client: the configurable token-endpoint auth method
/// and tolerance of optional / non-rotating refresh tokens and missing expires_in.
/// </summary>
public class OidcClientTests
{
    private sealed class RecordingHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? CapturedBody
        {
            get; private set;
        }

        public System.Net.Http.Headers.AuthenticationHeaderValue? CapturedAuth
        {
            get; private set;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.CapturedAuth = request.Headers.Authorization;
            this.CapturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static OidcClient CreateSut(RecordingHandler handler, OidcSettings settings) =>
        new(new HttpClient(handler), Options.Create(settings), new Mock<ILogger<OidcClient>>().Object);

    private static OidcSettings BaseSettings(string authMethod) => new()
    {
        ClientId = "client-abc",
        ClientSecret = "secret-xyz",
        TokenUrl = "https://idp.example/token",
        UserInfoUrl = "https://idp.example/userinfo",
        TokenEndpointAuthMethod = authMethod,
        UsePkce = false,
    };

    [Fact]
    public async Task ClientSecretPost_PutsCredentialsInBody_NoBasicHeader()
    {
        var handler = new RecordingHandler("""{"access_token":"at","refresh_token":"rt","expires_in":1800}""");
        var sut = CreateSut(handler, BaseSettings("client_secret_post"));

        var result = await sut.ExchangeCodeAsync("code", "https://rp/cb", null);

        Assert.NotNull(result);
        Assert.Null(handler.CapturedAuth);
        Assert.Contains("client_id=client-abc", handler.CapturedBody);
        Assert.Contains("client_secret=secret-xyz", handler.CapturedBody);
    }

    [Fact]
    public async Task ClientSecretBasic_SetsBasicHeader_OmitsSecretFromBody()
    {
        var handler = new RecordingHandler("""{"access_token":"at","refresh_token":"rt","expires_in":1800}""");
        var sut = CreateSut(handler, BaseSettings("client_secret_basic"));

        var result = await sut.RefreshAsync("old-rt");

        Assert.NotNull(result);
        Assert.NotNull(handler.CapturedAuth);
        Assert.Equal("Basic", handler.CapturedAuth!.Scheme);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("client-abc:secret-xyz"));
        Assert.Equal(expected, handler.CapturedAuth.Parameter);
        Assert.DoesNotContain("client_secret=", handler.CapturedBody);
        Assert.Contains("client_id=client-abc", handler.CapturedBody);
    }

    [Fact]
    public async Task NonRotatingProvider_ReturnsNullRefreshToken()
    {
        var handler = new RecordingHandler("""{"access_token":"at","expires_in":1800}""");
        var sut = CreateSut(handler, BaseSettings("client_secret_post"));

        var result = await sut.RefreshAsync("old-rt");

        Assert.NotNull(result);
        Assert.Equal("at", result!.AccessToken);
        Assert.Null(result.RefreshToken);
        Assert.Equal(1800, result.ExpiresIn);
    }

    [Fact]
    public async Task MissingExpiresIn_ReturnsNullExpiry()
    {
        var handler = new RecordingHandler("""{"access_token":"at"}""");
        var sut = CreateSut(handler, BaseSettings("client_secret_post"));

        var result = await sut.ExchangeCodeAsync("code", "https://rp/cb", null);

        Assert.NotNull(result);
        Assert.Null(result!.ExpiresIn);
    }

    [Fact]
    public async Task MissingAccessToken_ReturnsNull()
    {
        var handler = new RecordingHandler("""{"refresh_token":"rt"}""");
        var sut = CreateSut(handler, BaseSettings("client_secret_post"));

        var result = await sut.ExchangeCodeAsync("code", "https://rp/cb", null);

        Assert.Null(result);
    }

    [Fact]
    public async Task ErrorResponse_ReturnsNull()
    {
        var handler = new RecordingHandler("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);
        var sut = CreateSut(handler, BaseSettings("client_secret_post"));

        var result = await sut.RefreshAsync("old-rt");

        Assert.Null(result);
    }
}
