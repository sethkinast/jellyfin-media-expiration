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
    public string Description => "Deletes movies and TV shows that have exceeded their expiration period.";
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
            var displayName = entry.Item.Name;

            _logger.LogInformation(
                "Deleting expired {Type}: '{Name}' (last watched {LastWatched}, expired {ExpiresAt:d})",
                entry.Item.GetType().Name, displayName,
                entry.LastWatched.HasValue ? entry.LastWatched.Value.ToString("d") : "never",
                entry.ExpiresAt);

            if (Plugin.Instance!.Configuration.DryRun)
            {
                _logger.LogInformation("Dry run enabled. Skipping actual deletion for '{Name}'.", displayName);
            }
            else
            {
                try
                {
                    // For a show, delete every episode, then every season, then the
                    // series itself so no empty season/series shell is left behind.
                    if (entry.Item is Series series)
                    {
                        var seasons = _libraryManager.GetItemList(new InternalItemsQuery
                        {
                            ParentId = series.Id,
                            IncludeItemTypes = new[] { BaseItemKind.Season },
                            IsVirtualItem = false
                        }, false);

                        foreach (var season in seasons)
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

                            _libraryManager.DeleteItem(season, new DeleteOptions
                            {
                                DeleteFileLocation = true
                            });
                        }

                        _libraryManager.DeleteItem(series, new DeleteOptions
                        {
                            DeleteFileLocation = true
                        });
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