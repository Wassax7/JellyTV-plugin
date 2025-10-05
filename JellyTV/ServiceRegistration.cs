using Jellyfin.Plugin.JellyTV.EntryPoints;
using Jellyfin.Plugin.JellyTV.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyTV;

/// <summary>
/// Registers JellyTV services with the Jellyfin host.
/// </summary>
public sealed class ServiceRegistration : IPluginServiceRegistrator
{
    /// <summary>
    /// Registers JellyTV services into the host service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    public void RegisterServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<JellyTVPushService>();
        serviceCollection.AddSingleton<JellyTVEpisodeBatcher>();
        serviceCollection.AddHostedService<JellyTVEventListener>();
    }

    /// <summary>
    /// Registers JellyTV services into the host service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <param name="applicationHost">The server application host.</param>
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        RegisterServices(serviceCollection);
    }
}
