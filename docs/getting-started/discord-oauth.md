# Discord OAuth2 Setup

PoracleWeb uses Discord OAuth2 for user authentication. This page walks through creating and configuring a Discord application.

## Create a Discord application

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications)
2. Click **New Application** and give it a name
3. Under **OAuth2**, add redirect URIs:

    | Environment | Redirect URI |
    |---|---|
    | Production | `http://your-domain:8082/auth/discord/callback` |
    | Development | `http://localhost:4200/auth/discord/callback` |

4. Copy the **Client ID** and **Client Secret**

## Optional: Create a bot

Creating a bot under the same application enables:

- **Avatar display** — User avatars shown in the UI
- **Geofence forum posts** — Automatic Discord forum threads for geofence submissions

### Bot permissions for geofence forum

If using the geofence submission feature with Discord forum integration, the bot needs these permissions on the forum channel:

| Permission | Purpose |
|---|---|
| View Channel | Access the forum channel |
| Send Messages in Threads | Post status updates in threads |
| Manage Threads | Lock and archive threads on approval/rejection |
| Manage Channels | Auto-create forum tags (Pending/Approved/Rejected) |

!!! tip
    If the bot doesn't have **Manage Channels** permission, create the forum tags (Pending, Approved, Rejected) manually on the channel.

## Configuration

=== "Docker (.env)"

    ```env
    DISCORD_CLIENT_ID=your_discord_client_id
    DISCORD_CLIENT_SECRET=your_discord_client_secret
    DISCORD_BOT_TOKEN=your_discord_bot_token
    DISCORD_GUILD_ID=your_discord_server_id
    DISCORD_GEOFENCE_FORUM_CHANNEL_ID=your_forum_channel_id
    ```

=== "Development (appsettings.Development.json)"

    ```json
    {
      "Discord": {
        "ClientId": "your_discord_client_id",
        "ClientSecret": "your_discord_client_secret",
        "RedirectUri": "http://localhost:4200/auth/discord/callback",
        "FrontendUrl": "http://localhost:4200",
        "BotToken": "your_discord_bot_token",
        "GuildId": "your_discord_guild_id",
        "GeofenceForumChannelId": ""
      }
    }
    ```

!!! warning "Discord API domain"
    PoracleWeb uses `discordapp.com` (not `discord.com`) for API calls. The `discord.com` domain is blocked by Cloudflare in some server environments. This is already configured in the application — no action needed.
