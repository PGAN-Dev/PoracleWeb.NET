namespace Pgan.PoracleWebNet.Core.Models;

public enum LinkSide
{
    Discord,
    Telegram,
}

/// <summary>One person's paired Discord and Telegram identities.</summary>
public sealed record AccountLink(
    int Id,
    string DiscordId,
    string TelegramId,
    LinkSide DefaultDestination,
    DateTime? DiscordVerifiedAt,
    DateTime? TelegramVerifiedAt,
    DateTime CreatedAt);

public enum PauseReason
{
    None,
    NotRegistered,
    AdminDisabled,
    Stopped,
}

/// <summary>One half of a link as resolved now: its human id, whether that human exists, and why it is paused.</summary>
public sealed record PairSide(LinkSide Side, string HumanId, bool Exists, PauseReason Pause);

/// <summary>A caller's link resolved against PoracleNG. <see cref="Single"/> is the unlinked answer.</summary>
public sealed record LinkedPair(string CallerId, AccountLink? Link, PairSide? Discord, PairSide? Telegram)
{
    public bool IsLinked => this.Link is not null;

    /// <summary>Human ids of the sides that exist, the caller's own first.</summary>
    public IReadOnlyList<string> ExistingHumanIds
    {
        get
        {
            if (this.Link is null)
            {
                return [this.CallerId];
            }

            var ids = new List<string>(2);
            foreach (var side in new[] { this.Discord, this.Telegram })
            {
                if (side is { Exists: true } && !ids.Contains(side.HumanId))
                {
                    ids.Add(side.HumanId);
                }
            }

            var callerIndex = ids.IndexOf(this.CallerId);
            if (callerIndex > 0)
            {
                ids.RemoveAt(callerIndex);
                ids.Insert(0, this.CallerId);
            }

            return ids;
        }
    }

    public string? HumanIdFor(LinkSide side) => this.Link is null
        ? null
        : side == LinkSide.Discord ? this.Link.DiscordId : this.Link.TelegramId;

    public LinkSide? SideOf(string humanId)
    {
        if (this.Link is null)
        {
            return null;
        }

        if (string.Equals(this.Link.DiscordId, humanId, StringComparison.Ordinal))
        {
            return LinkSide.Discord;
        }

        return string.Equals(this.Link.TelegramId, humanId, StringComparison.Ordinal) ? LinkSide.Telegram : null;
    }

    public static LinkedPair Single(string callerId) => new(callerId, null, null, null);
}
