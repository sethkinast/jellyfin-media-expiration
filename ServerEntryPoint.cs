// ServerEntryPoint.cs
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaExpiration;

public class ServerEntryPoint : IHostedService
{
    private readonly ILogger<ServerEntryPoint> _logger;

    public ServerEntryPoint(ILogger<ServerEntryPoint> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Plugin.Instance!.RegisterWithJavaScriptInjector(_logger);
        Plugin.Instance!.RegisterWithHomeScreenSections(_logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}