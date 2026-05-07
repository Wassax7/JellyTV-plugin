# JellyTV plugin

Allows your Jellyfin server to communicate with [JellyTV app](https://jellytv.app).

## Build and Install Manually

1. Install [.NET Core SDK](https://dotnet.microsoft.com/download)

2. Build the plugin with the following command:

```
dotnet publish --configuration Release
```

3. Place the resulting .dll file from `bin/Release/net9.0/publish` into its own folder inside the Jellyfin plugin directory.

## Configure Webhooks

The JellyTV plugin ships with a built-in webhook route that you can use in other programs such as Seerr to trigger push notifications alongside the standard Jellyfin events.

### 1. Add the route

```
{yourJellyfinUrl}/JellyTV/notifications
```

### 2. Provide the desired JSON payload (single user example)

```json
[
  {
    "title": "JellyTV",
    "body": "A user has requested a movie",
    "username": "Charly"
  }
]
```

Or target multiple specific users:

```json
[
  {
    "title": "JellyTV",
    "body": "A user has requested a movie",
    "username": ["Charly", "Jamie"]
  }
]
```

Notes:

- `username` is optional. When omitted the notification is sent to every registered JellyTV device. Supply either a single username/userId string or an array of strings to deliver to specific users.
- The request must include a valid Jellyfin access token that belongs to an administrator account; the `JellyTV/notifications` endpoint ignores non-admin callers.
- Keep `title` ≤ 128 characters and `body` ≤ 512 characters to avoid being rejected by the relay.

### 3. Add the following headers

```
Content-Type: application/json
X-Emby-Token: {jellyfinToken}
```

### Example

For Seerr:

```json
[
  {
    "title": "New request from {{requestedBy_username}}",
    "body": "{{subject}}"
  }
]
```

---

## Configure Arr Webhooks

JellyTV also accepts native webhook payloads from the Servarr stack, like Radarr and Sonarr.

1. In Jellyfin, open Dashboard > Plugins > JellyTV > Settings.
2. Generate a URL token if needed, and save.
3. Copy the generated webhook URL.
4. In each Arr app, go to Settings > Connect > Add > Webhook.
5. Set the URL to the copied JellyTV URL, method to `POST`, and enable the event triggers you want.

The native endpoint is:

```
{yourJellyfinUrl}/JellyTV/notifications?token={sharedSecret}
```

The secret can also be sent as `X-JellyTV-Webhook-Key: {sharedSecret}` or as a bearer token.

Native Arr webhook events are forwarded to all registered JellyTV devices. Event filtering is controlled in each Arr app's Webhook connection settings.

---

### Want to join our community?

Help us grow, ask for new features and get support 🚀

[Join discord](https://discord.gg/zftHyqb4EB)
