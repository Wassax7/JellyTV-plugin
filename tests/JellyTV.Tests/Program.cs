using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellyTV;
using Jellyfin.Plugin.JellyTV.Api;
using Jellyfin.Plugin.JellyTV.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging.Abstractions;

// Exercise the real /JellyTV/notifications route, controller, store and push service.
// Jellyfin authentication/user lookup and relay transport are local test doubles.
Console.WriteLine("Initializing webhook tests...");
var directory = Path.Combine(Path.GetTempPath(), "jellytv-webhook-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var paths = Stub<IApplicationPaths>.Create((method, _) => method.Name.StartsWith("get_", StringComparison.Ordinal) ? directory : throw new NotSupportedException());
Console.WriteLine("Creating plugin fixture...");
var plugin = new Plugin(paths, Stub<IXmlSerializer>.Create((_, _) => throw new NotSupportedException()));
Console.WriteLine("Creating test users...");
var users = Enumerable.Range(1, 205).Select(i => new User("user" + i, "test", "test") { Id = Guid.NewGuid() }).ToArray();
string Token(int i) => i.ToString("x64");
void Store(bool sharedTokens = false, bool noTokens = false)
{
    File.WriteAllText(Path.Combine(plugin.DataDirectory, "registered-users.json"), JsonSerializer.Serialize(users.Select((user, i) => new
    {
        UserId = user.Id.ToString("N"),
        Tokens = noTokens ? Array.Empty<string>() : new[] { Token(sharedTokens && i == 1 ? 1 : i + 1) }
    })));
}
Store();
var userManager = Stub<IUserManager>.Create((method, args) => method.Name switch
{
    "GetUserByName" => users.FirstOrDefault(u => string.Equals(u.Username, (string?)args![0], StringComparison.OrdinalIgnoreCase)),
    "GetUserById" => users.FirstOrDefault(u => u.Id == (Guid)args![0]!),
    _ => throw new NotSupportedException(method.Name)
});
var authorization = new AuthorizationInfo { Token = "test", IsApiKey = true, IsAuthenticated = true };
var auth = Stub<IAuthorizationContext>.Create((method, _) => method.Name == "GetAuthorizationInfo"
    ? Task.FromResult(authorization) : throw new NotSupportedException(method.Name));
using var relay = new Relay();
using var relayClient = new HttpClient(relay);
var push = new JellyTVPushService(NullLogger<JellyTVPushService>.Instance,
    Stub<IServerConfigurationManager>.Create((_, _) => null), relayClient);
using var rateLimit = new RateLimitService();
Console.WriteLine("Creating host...");
var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args, ContentRootPath = directory });
Console.WriteLine("Host builder created.");
builder.Logging.ClearProviders();
builder.WebHost.UseKestrel();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddControllers().AddApplicationPart(typeof(JellyTVNotificationsController).Assembly);
builder.Services.AddSingleton(push);
builder.Services.AddSingleton(userManager);
builder.Services.AddSingleton(auth);
builder.Services.AddSingleton(rateLimit);
builder.Services.AddSingleton<ArrWebhookNotificationFormatter>();
Console.WriteLine("Building host...");
await using var app = builder.Build();
app.MapControllers();
try
{
    Console.WriteLine("Starting local webhook host...");
    await app.StartAsync();
    Console.WriteLine("Local webhook host started.");
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient { BaseAddress = new Uri(address) };
    async Task<JsonElement> Post(string json, int processed, int skipped = 0)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/JellyTV/notifications")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Emby-Token", "test");
        using var response = await client.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.OK, "HTTP 200: " + await response.Content.ReadAsStringAsync());
        var result = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Check(result.GetProperty("processed").GetInt32() == processed, "processed counts original accepted entries");
        Check(result.GetProperty("skipped").GetInt32() == skipped, "skipped counts unknown names per entry");
        return result;
    }
    async Task Case(string name, Func<Task> test)
    {
        relay.Messages.Clear();
        await test();
        Console.WriteLine("PASS " + name);
    }
    await Case("overlap, GUID/name aliases and normalized message", async () =>
    {
        await Post($$"""[{"title":" A ","body":" B ","username":["user1","user2","USER1"]},{"title":"A","body":"B","username":["{{users[1].Id}}","user3"]}]""", 2);
        Check(relay.Messages.Count == 1, "one POST for identical message");
        Check(Tokens(relay.Messages[0]).SequenceEqual(new[] { Token(1), Token(2), Token(3) }), "recipient union");
    });
    await Case("distinct title/body and first occurrence order", async () =>
    {
        await Post("""[{"title":"A","body":"B","username":"user1"},{"title":"C","body":"B","username":"user1"},{"title":"A","body":"D","username":"user2"},{"title":"A","body":"B","username":"user2"}]""", 4);
        Check(relay.Messages.Select(m => (m.GetProperty("title").GetString()!, m.GetProperty("body").GetString()!)).SequenceEqual(new[] { ("A", "B"), ("C", "B"), ("A", "D") }), "distinct messages retain order");
    });
    await Case("unknown targets never broadcast", async () =>
    {
        await Post("""[{"title":"A","username":"missing"},{"title":"A","username":["missing","user1"]},{"title":"A","username":"missing"}]""", 1, 3);
        Check(relay.Messages.Count == 1 && Tokens(relay.Messages[0]).SequenceEqual(new[] { Token(1) }), "only resolved recipient");
    });
    await Case("existing empty-audience broadcast forms, merged and capped", async () =>
    {
        await Post("""[{"title":"A"},{"title":"A","username":null},{"title":"A","username":[]},{"title":"A","username":" "},{"title":"A","username":[null,42," "]},{"title":"A","username":42},{"title":"A","username":"missing"},{"title":"A","username":"user1"}]""", 7, 1);
        Check(relay.Messages.Select(m => Tokens(m).Length).SequenceEqual(new[] { 100, 100, 5 }), "broadcast token batches");
        Check(relay.Messages.SelectMany(Tokens).Distinct().Count() == 205, "all recipients exactly once");
    });
    await Case("formerly valid targeted sends merge above 100", async () =>
    {
        var entries = users.Select(u => new { title = "A", body = "B", username = u.Username });
        await Post(JsonSerializer.Serialize(entries), 205);
        var tokens = relay.Messages.SelectMany(Tokens).ToArray();
        Check(relay.Messages.Count == 3 && tokens.Length == 205 && tokens.Distinct().Count() == 205, "205 tokens delivered once in three POSTs");
    });
    foreach (var count in new[] { 100, 101 })
    {
        await Case("token boundary " + count, async () =>
        {
            await Post(JsonSerializer.Serialize(users.Take(count).Select(u => new { title = "A", username = u.Username })), count);
            Check(relay.Messages.Count == (count == 100 ? 1 : 2), "boundary POST count");
        });
    }
    await Case("shared device tokens deduped after union", async () =>
    {
        Store(sharedTokens: true);
        await Post("""[{"username":"user1"},{"username":"user2"}]""", 2);
        Check(relay.Messages.Count == 1 && Tokens(relay.Messages[0]).Length == 1, "shared token sent once");
        Store();
    });
    await Case("trim and truncation define outgoing identity", async () =>
    {
        var entries = new[] { new { title = new string('a', 201), body = new string('b', 4001), username = "user1" }, new { title = new string('a', 200), body = new string('b', 4000), username = "user2" } };
        await Post(JsonSerializer.Serialize(entries), 2);
        Check(relay.Messages.Count == 1 && relay.Messages[0].GetProperty("title").GetString()!.Length == 200 && relay.Messages[0].GetProperty("body").GetString()!.Length == 4000, "normalized message");
    });
    await Case("empty token set keeps counters", async () =>
    {
        Store(noTokens: true);
        await Post("""[{"username":"user1"},{"username":"user1"}]""", 2);
        Check(relay.Messages.Count == 0, "no empty relay request");
        Store();
    });
    await Case("separate webhooks remain separate", async () =>
    {
        await Post("""[{"username":"user1"}]""", 1);
        await Post("""[{"username":"user1"}]""", 1);
        Check(relay.Messages.Count == 2, "no cross-request coalescing");
    });
    await Case("validation and authentication", async () =>
    {
        using var missingAuth = await client.PostAsync("/JellyTV/notifications", new StringContent("[{}]", Encoding.UTF8, "application/json"));
        Check(missingAuth.StatusCode == HttpStatusCode.Unauthorized, "missing authentication denied");
        using var invalid = await client.PostAsync("/JellyTV/notifications", new StringContent("[]", Encoding.UTF8, "application/json"));
        Check(invalid.StatusCode == HttpStatusCode.BadRequest, "empty array rejected");
        authorization.Token = null;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/JellyTV/notifications") { Content = new StringContent("[{}]", Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Emby-Token", "invalid");
        using var response = await client.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.Unauthorized && relay.Messages.Count == 0, "invalid token denied");
    });
    Console.WriteLine("All webhook HTTP tests passed; no production requests.");
}
finally
{
    await app.StopAsync();
    Directory.Delete(directory, recursive: true);
}
static string[] Tokens(JsonElement message) => message.GetProperty("deviceTokens").EnumerateArray().Select(t => t.GetString()!).ToArray();
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
public class Stub<T> : DispatchProxy where T : class
{
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
    public static T Create(Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = DispatchProxy.Create<T, Stub<T>>();
        ((Stub<T>)(object)proxy).Handler = handler;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
}
sealed class Relay : HttpMessageHandler
{
    public List<JsonElement> Messages { get; } = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
        var count = message.GetProperty("deviceTokens").GetArrayLength();
        if (request.Method != HttpMethod.Post || request.RequestUri!.AbsoluteUri != "https://push.jellytv.app/api/push" || count is < 1 or > 100 || message.EnumerateObject().Count() != 3)
            throw new InvalidOperationException("Unexpected relay contract");
        Messages.Add(message);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"results\":[]}") };
    }
}
