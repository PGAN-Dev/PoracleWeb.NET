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
    callback from the incoming request. In production the API and the SPA share an origin, so this
    is simply your domain. In development the browser is on the Angular dev server (4200) and
    `proxy.conf.json` sets `changeOrigin: false`, so the `Host` stays `localhost:4200` — register that,
    not the API's 5048. If you serve the dev app on another port, register that port instead.

    !!! tip "Behind a reverse proxy, set `PUBLIC_URL`"
        Deriving the callback from the request goes wrong when TLS is terminated in front of the app:
        it sees plain HTTP and builds an `http://` callback that Discord rejects as unregistered.
        Setting `PUBLIC_URL=https://poracle.example.com` in `.env` pins the callback to exactly what
        you registered here, whatever the request looks like. Declaring the proxy with
        `PROXY_KNOWN_PROXIES` / `PROXY_KNOWN_NETWORKS` fixes the same thing at source and additionally
        keeps rate limits per-user — see [Behind a reverse proxy](standalone-setup.md#reverse-proxy-optional).

4. Copy the **Client ID** and **Client Secret**

## Optional: Create a bot

A bot under the same application does two things. It opens the Discord forum threads for geofence
submissions, and it reads guild membership so role gating can restrict sign-in to holders of a role.
Role gating needs three pieces: the `enable_roles` site setting switched on, the role IDs in
`allowed_role_ids`, and the bot in the guild you name in `DISCORD_GUILD_ID`. With `enable_roles` off
the role IDs are never read, so sign-in stays open to everyone.

Avatars do not need it. They arrive with the OAuth login and are cached from there.

### Bot permissions for geofence forum

If using the geofence submission feature with Discord forum integration, the bot needs these permissions on the forum channel:

| Permission | Purpose |
|---|---|
| View Channel | Access the forum channel |
| Create Posts | Open the submission thread |
| Send Messages in Threads | Post status updates in threads |
| Attach Files | Upload the geofence map image into the post |
| Manage Threads | Lock and archive threads on approval/rejection |
| Manage Channels | Auto-create forum tags (Pending/Approved/Rejected) |

!!! tip
    If the bot doesn't have **Manage Channels** permission, create the forum tags (Pending, Approved, Rejected) manually on the channel.

## Configuration

=== ".env file"

    `./scripts/setup.sh` prompts for the first three. The guild and forum channel IDs are not part of
    the wizard — add them to `.env` by hand if you need them.

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
