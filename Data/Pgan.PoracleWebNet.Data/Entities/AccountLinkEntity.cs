using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Data.Entities;

[Table("account_links")]
public class AccountLinkEntity
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id
    {
        get; set;
    }

    [Column("discord_id")]
    [Required]
    public string DiscordId { get; set; } = string.Empty;

    [Column("telegram_id")]
    [Required]
    public string TelegramId { get; set; } = string.Empty;

    [Column("default_destination")]
    [Required]
    public string DefaultDestination { get; set; } = AccountLinkKeys.Wire(LinkSide.Discord);

    [Column("discord_verified_at")]
    public DateTime? DiscordVerifiedAt
    {
        get; set;
    }

    [Column("telegram_verified_at")]
    public DateTime? TelegramVerifiedAt
    {
        get; set;
    }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
