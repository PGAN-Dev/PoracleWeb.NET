using Pgan.PoracleWebNet.Api.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class AvatarCacheServiceTests
{
    [Theory]
    [InlineData("telegram:user")]
    [InlineData("telegram:channel")]
    [InlineData("webhook")]
    public void NonDiscordTypesGetNoDiscordImage(string type) =>
        Assert.Equal(string.Empty, AvatarCacheService.GetAvatarOrDefault("tg-none-" + type, type));

    [Fact]
    public void DiscordTypesKeepTheDiscordDefault()
    {
        Assert.StartsWith("https://cdn.discordapp.com/embed/avatars/", AvatarCacheService.GetAvatarOrDefault("123456789012345678", "discord:user"), StringComparison.Ordinal);
        Assert.StartsWith("https://cdn.discordapp.com/embed/avatars/", AvatarCacheService.GetAvatarOrDefault("123456789012345678"), StringComparison.Ordinal);
    }

    [Fact]
    public void ACachedTelegramPhotoWinsOverTheDefault()
    {
        Assert.True(AvatarCacheService.TryCacheProfilePhoto("tg-photo-1", "https://t.me/i/userpic/320/abc.jpg"));
        Assert.Equal("https://t.me/i/userpic/320/abc.jpg", AvatarCacheService.GetAvatarOrDefault("tg-photo-1", "telegram:user"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://t.me/i/userpic/320/abc.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public void OnlyHttpsPhotosAreCached(string? url)
    {
        Assert.False(AvatarCacheService.TryCacheProfilePhoto("tg-photo-bad", url));
        Assert.Equal(string.Empty, AvatarCacheService.GetAvatarOrDefault("tg-photo-bad", "telegram:user"));
    }
}
