# JellyTV plugin

Allows your Jellyfin server to communicate with [JellyTV app](https://jellytv.app).

## Build and Install Manually

1. Install [.NET Core SDK](https://dotnet.microsoft.com/download)

2. Build the plugin with the following command:

```
dotnet publish --configuration Release
```

3. Place the resulting .dll file from `bin/Release/net8.0/publish` into its own folder inside the Jellyfin plugin directory.

## Configure Webhooks

The JellyTV plugin ships with a built-in webhook route that you can use in other programs such as Jellyseerr to trigger push notifications alongside the standard Jellyfin events.

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

For Jellyseerr :

```json
[
  {
    "title": "New request from {{requestedBy_username}}",
    "body": "{{subject}}"
  }
]
```

---

### Want to join our community?

Help us grow, ask for new features and get support 🚀

[Join discord](https://discord.gg/zftHyqb4EB)
