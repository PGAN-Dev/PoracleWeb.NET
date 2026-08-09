# Discord OAuth2 Setup

PoracleWeb.NET uses Discord OAuth2 for user authentication. This page walks through creating and configuring a Discord application.

## Create a Discord application

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications)
2. Click **New Application** and give it a name
3. Under **OAuth2**, add redirect URIs:

    | Environment | Redirect URI |
    |---|---|
    | Production / Docker | `http://your-domain:8082/api/auth/discord/callback` |
    | Development | `http://localhost:4200/api/auth/discord/callback` |

    The redirect URI must match the origin **the browser is on**, because `AuthController` builds the
    callback from the incoming `Host` header. In production the API and the SPA share an origin, so this
    is simply your domain. In development the browser is on the Angular dev server (4200) and
    `proxy.conf.json` sets `changeOrigin: false`, so the `Host` stays `localhost:4200` — register that,
    not the API's 5048. If you serve the dev app on another port, register that port instead.

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

=== ".env file"

    These values are set during `./scripts/setup.sh`, or you can edit `.env` directly:

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
        "FrontendUrl": "http://localhost:4200",
        "BotToken": "your_discord_bot_token",
        "GuildId": "your_discord_guild_id",
        "GeofenceForumChannelId": ""
      }
    }
    ```

!!! warning "Discord API domain"
    PoracleWeb.NET uses `discordapp.com` (not `discord.com`) for API calls. The `discord.com` domain is blocked by Cloudflare in some server environments. This is already configured in the application — no action needed.
