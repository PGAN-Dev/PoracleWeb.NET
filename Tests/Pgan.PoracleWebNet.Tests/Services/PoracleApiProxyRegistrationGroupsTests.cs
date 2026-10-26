using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The Telegram registration groups, read from <c>GET /api/config/values</c>. Shapes follow PoracleNG's
/// <c>ExtractValues</c> (config_values.go): <c>telegram.channels</c> is a string array, and each
/// <c>area_security.communities</c> row is flattened to <c>{ name, telegram_channels, ... }</c>. See #1056.
/// </summary>
public class PoracleApiProxyRegistrationGroupsTests
{
    private static PoracleApiProxy CreateSut(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = "http://localhost:3030",
                ["Poracle:ApiSecret"] = "test-secret",
            })
            .Build());

    [Fact]
    public async Task AreaSecurityOff_ReadsTheTopLevelChannels()
    {
        var sut = CreateSut(new Fixed(HttpStatusCode.OK, /*lang=json,strict*/ """
            {"values":{"telegram":{"channels":["-1001","-1002"]},
                       "area_security":{"enabled":false,"communities":[{"name":"North","telegram_channels":["-1003"]}]}}}
            """));

        var groups = await sut.GetTelegramRegistrationGroupsAsync();

        Assert.NotNull(groups);
        Assert.False(groups.AreaSecurity);
        Assert.Equal(["-1001", "-1002"], groups.Channels);
    }

    [Fact]
    public async Task AreaSecurityOn_ReadsEachCommunity_WithItsNameLowercased()
    {
        var sut = CreateSut(new Fixed(HttpStatusCode.OK, /*lang=json,strict*/ """
            {"values":{"telegram":{"channels":["-1001"]},
                       "area_security":{"enabled":true,"communities":[
                         {"name":"Northside","telegram_channels":["-1003","-1004"],"discord_channels":["9"]},
                         {"name":"Riverside","telegram_channels":[]}]}}}
            """));

        var groups = await sut.GetTelegramRegistrationGroupsAsync();

        Assert.NotNull(groups);
        Assert.True(groups.AreaSecurity);
        Assert.Collection(groups.Communities,
            c =>
            {
                Assert.Equal("northside", c.Name);
                Assert.Equal(["-1003", "-1004"], c.Channels);
            },
            c =>
            {
                Assert.Equal("riverside", c.Name);
                Assert.Empty(c.Channels);
            });
    }

    [Fact]
    public async Task AbsentSections_AreEmptyNotAnError()
    {
        var sut = CreateSut(new Fixed(HttpStatusCode.OK, /*lang=json,strict*/ """{"values":{}}"""));

        var groups = await sut.GetTelegramRegistrationGroupsAsync();

        Assert.NotNull(groups);
        Assert.False(groups.AreaSecurity);
        Assert.Empty(groups.Channels);
        Assert.Empty(groups.Communities);
    }

    [Fact]
    public async Task ANon2xxAnswer_Throws_SoTheCallerCanTreatItAsUnreadable() =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateSut(new Fixed(HttpStatusCode.ServiceUnavailable, "{}")).GetTelegramRegistrationGroupsAsync());

    private sealed class Fixed(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
