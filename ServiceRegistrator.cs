using Jellyfin.Plugin.MediaExpiration.Providers;
using Jellyfin.Plugin.MediaExpiration.ScheduledTasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MediaExpiration;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddScoped<ExpirationManager>();
        serviceCollection.AddScoped<MediaExpirationTask>();
        serviceCollection.AddScoped<ExpiringSoonCollectionTask>();
        serviceCollection.AddHostedService<ServerEntryPoint>();
    }
}