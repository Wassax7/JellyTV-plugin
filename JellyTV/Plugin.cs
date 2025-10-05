using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.JellyTV.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyTV;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        // Expose a persistent directory for this plugin (under plugin configurations path).
        DataDirectory = Path.Combine(applicationPaths.PluginConfigurationsPath, Name);
        try
        {
            Directory.CreateDirectory(DataDirectory);
        }
        catch
        {
            // ignore
        }
    }

    /// <inheritdoc />
    public override string Name => "JellyTV";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("eb5d7894-8eef-4b36-aa6f-5d124e828ce1");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Gets the plugin's persistent data directory.
    /// </summary>
    public string DataDirectory { get; }

    // No extra methods needed; use UpdateConfiguration(Configuration) where required.

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }

    /// <summary>
    /// Returns the embedded thumbnail image stream if available.
    /// </summary>
    /// <returns>The thumbnail stream, or <see cref="Stream.Null"/> when the resource is missing.</returns>
    public Stream? GetThumbImage()
    {
        // The dashboard expects this resource when rendering the extensions list thumbnail.
        return GetType().Assembly.GetManifestResourceStream("Jellyfin.Plugin.JellyTV.Resources.thumb.png") ?? Stream.Null;
    }
}
