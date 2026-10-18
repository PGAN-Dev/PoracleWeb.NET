using System.Security.Cryptography;
using System.Text;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// The opaque id the API gives a linked game account in place of its player id.
/// </summary>
/// <remarks>
/// <para>
/// A player id is the input to a person's shiny rolls, so it never leaves the server: the SPA lists, relabels
/// and unlinks accounts by this handle, and the server finds the account again by recomputing the handle
/// for each of the human's accounts.
/// </para>
/// <para>
/// HMAC-SHA256 over the human id and the player id, keyed from the JWT secret, truncated to 128 bits. Keyed
/// so a handle cannot be checked against guessed player ids, and stable across restarts because the JWT
/// secret already has to be. The human id is part of the input so one player id linked to two people gives
/// two unrelated handles.
/// </para>
/// </remarks>
public sealed class PlayerAccountHandles
{
    private static readonly byte[] KeyInfo = Encoding.UTF8.GetBytes("PoracleWeb.NET player account handle v1");
    private readonly byte[] _key;

    private PlayerAccountHandles(byte[] key) => this._key = key;

    /// <summary>Derives the handle key from the JWT secret, so this needs no setting of its own.</summary>
    public static PlayerAccountHandles FromSecret(string secret) =>
        new(HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32, info: KeyInfo));

    /// <summary>The handle of <paramref name="playerId"/> as linked to <paramref name="humanId"/>.</summary>
    public string For(string humanId, string playerId)
    {
        var mac = HMACSHA256.HashData(this._key, Encoding.UTF8.GetBytes($"{humanId}\0{playerId}"));
        return Convert.ToBase64String(mac, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
