namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>One area-security community and the Telegram groups that place a person in it. <paramref name="Name"/> is lowercase.</summary>
public sealed record TelegramCommunityGroups(string Name, IReadOnlyList<string> Channels);

/// <summary>
/// The Telegram groups PoracleNG registers people from, as its effective config reports them: <c>telegram.channels</c>
/// when area security is off, each community's <c>telegram_channels</c> when it is on.
/// </summary>
public sealed record TelegramRegistrationGroups(
    bool AreaSecurity,
    IReadOnlyList<string> Channels,
    IReadOnlyList<TelegramCommunityGroups> Communities);

/// <summary>Whether Poracle's <c>/poracle</c> would admit a person, and the communities it would record (empty without area security).</summary>
public sealed record TelegramQualification(bool Qualified, IReadOnlyList<string> Communities)
{
    public static TelegramQualification No { get; } = new(false, []);
}
