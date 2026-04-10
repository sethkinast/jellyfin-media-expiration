#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Jellyfin.Plugin.MediaExpiration.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaExpiration;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public override string Name => "Media Expiration";
    public override Guid Id => Guid.Parse("16445656-5f0a-4ea5-8e04-00d8f9c935c9");
    public override string Description => "Automatically expires and deletes media based on watch activity.";

    public const string ExpiringSoonCollectionName = "Expiring Soon";

    public static Plugin? Instance { get; private set; }

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "MediaExpiration",
                EmbeddedResourcePath = $"{GetType().Namespace}.Web.configurationpage.html",
                EnableInMainMenu = false
            },
            new PluginPageInfo
            {
                Name = "MediaExpirationJS",
                EmbeddedResourcePath = $"{GetType().Namespace}.Web.configurationpage.js"
            }
        };
    }

    public void RegisterWithHomeScreenSections(ILogger logger)
    {
        try
        {
            var homeScreenSectionsAssembly = AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.FullName?.Contains(".HomeScreenSections") ?? false);

            if (homeScreenSectionsAssembly is null)
            {
                logger.LogInformation("Home Screen Sections plugin not found — Expiring Soon home section disabled.");
                return;
            }

            var pluginInterfaceType = homeScreenSectionsAssembly.GetType("Jellyfin.Plugin.HomeScreenSections.PluginInterface");
            var registerMethod = pluginInterfaceType?.GetMethod("RegisterSection");

            if (registerMethod is null)
            {
                logger.LogWarning("Home Screen Sections RegisterSection method not found.");
                return;
            }

            var payload = new Newtonsoft.Json.Linq.JObject
            {
                { "id",              $"media-expiration-{Id}" },
                { "displayText",     ExpiringSoonCollectionName },
                { "limit",           1 },
                { "additionalData",  "" },
                { "resultsAssembly", GetType().Assembly.FullName },
                { "resultsClass",    "Jellyfin.Plugin.MediaExpiration.Providers.HomeSectionResultsHandler" },
                { "resultsMethod",   "GetResults" }
            };

            registerMethod.Invoke(null, new object?[] { payload });
            logger.LogInformation("Registered Expiring Soon home section with Home Screen Sections plugin.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register Expiring Soon home section.");
        }
    }

    public void RegisterWithJavaScriptInjector(ILogger logger)
    {
        try
        {
            var jsInjectorAssembly = AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.JavaScriptInjector") ?? false);

            if (jsInjectorAssembly is null)
            {
                logger.LogInformation("JavaScript Injector plugin not found — detail page badge disabled.");
                return;
            }

            var pluginInterfaceType = jsInjectorAssembly.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
            var registerMethod = pluginInterfaceType?.GetMethod("RegisterScript");

            if (registerMethod is null)
            {
                logger.LogWarning("JavaScript Injector RegisterScript method not found.");
                return;
            }

            var js = ReadEmbeddedResource("Web.detailbadge.js");

            var payload = new Newtonsoft.Json.Linq.JObject
            {
                { "id",                     $"{Id}-detail-badge" },
                { "name",                   "Media Expiration Badge" },
                { "script",                 js },
                { "enabled",                true },
                { "requiresAuthentication", true },
                { "pluginId",               Id.ToString() },
                { "pluginName",             Name }
            };

            var result = registerMethod.Invoke(null, new object[] { payload });
            logger.LogInformation("Registered detail badge with JavaScript Injector: {Result}", result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register with JavaScript Injector.");
        }
    }

    private string ReadEmbeddedResource(string resourceName)
    {
        var fullName = $"{GetType().Namespace}.{resourceName}";
        using var stream = GetType().Assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {fullName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}