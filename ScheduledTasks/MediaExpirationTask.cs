using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaExpiration.Providers;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaExpiration.ScheduledTasks;

public class MediaExpirationTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ExpirationManager _expirationManager;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<MediaExpirationTask> _logger;

    public string Name => "Expire Media";
    public string Key => "MediaExpirationExpireMedia";
    public string Description => "Deletes movies and TV seasons that have exceeded their expiration period.";
    public string Category => "Media Expiration";

    public MediaExpirationTask(
        ILibraryManager libraryManager,
        ExpirationManager expirationManager,
        ITaskManager taskManager,
        ILogger<MediaExpirationTask> logger)
    {
        _libraryManager = libraryManager;
        _expirationManager = expirationManager;
        _taskManager = taskManager;
        _logger = logger;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var config = Plugin.Instance!.Configuration;
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(config.ScheduledTaskHour).Ticks
            }
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Media Expiration task started.");

        // Gate on disk space
        if (!_expirationManager.IsDiskSpaceThresholdMet())
        {
            _logger.LogInformation("Disk space threshold not met. Skipping expiration run.");
            progress.Report(100);
            return;
        }

        var expired = _expirationManager.GetExpired().ToList();
        _logger.LogInformation("Found {Count} expired items to delete.", expired.Count);

        var anyDeleted = false;
        for (int i = 0; i < expired.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = expired[i];
            var displayName = entry.Item is Season s && s.Series is not null
                ? $"{s.Series.Name} - {entry.Item.Name}"
                : entry.Item.Name;

            _logger.LogInformation(
                "Deleting expired {Type}: '{Name}' (last watched {LastWatched}, expired {ExpiresAt:d})",
                entry.Item.GetType().Name, displayName,
                entry.LastWatched.HasValue ? entry.LastWatched.Value.ToString("d") : "never",
                entry.ExpiresAt);            if (Plugin.Instance!.Configuration.DryRun)
            {
                _logger.LogInformation("Dry run enabled. Skipping actual deletion for '{Name}'.", displayName);
            }
            else
            {
                try
                {
                    // For seasons, delete each episode's files then the season folder
                    if (entry.Item is Season season)
                    {
                        var episodes = _libraryManager.GetItemList(new InternalItemsQuery
                        {
                            ParentId = season.Id,
                            IncludeItemTypes = new[] { BaseItemKind.Episode },
                            IsVirtualItem = false
                        }, false);

                        foreach (var episode in episodes)
                        {
                            _libraryManager.DeleteItem(episode, new DeleteOptions
                            {
                                DeleteFileLocation = true
                            });
                        }
                    }
                    else
                    {
                        _libraryManager.DeleteItem(entry.Item, new DeleteOptions
                        {
                            DeleteFileLocation = true
                        });
                    }

                    anyDeleted = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete item '{Name}'.", entry.Item.Name);
                }
            }

            progress.Report((double)(i + 1) / expired.Count * 100);
        }

        // Immediately refresh the collection if anything was actually deleted
        if (anyDeleted)
            await UpdateExpiringSoonCollectionAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Media Expiration task completed.");
    }

    private Task UpdateExpiringSoonCollectionAsync(CancellationToken cancellationToken)
    {
        _taskManager.QueueScheduledTask<ExpiringSoonCollectionTask>(new TaskOptions());
        return Task.CompletedTask;
    }
}