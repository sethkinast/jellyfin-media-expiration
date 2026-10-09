using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.MediaExpiration.Configuration;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaExpiration.Providers;

public record ExpirationInfo(BaseItem Item, DateTime? LastWatched, DateTime ExpiresAt, int DaysRemaining);

public class ExpirationManager
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<ExpirationManager> _logger;

    public ExpirationManager(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILogger<ExpirationManager> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    /// <summary>
    /// Returns expiration info for all eligible items, ordered by soonest expiry.
    /// </summary>
    public IReadOnlyList<ExpirationInfo> GetExpirationCandidates()
    {
        var config = Plugin.Instance!.Configuration;
        var results = new List<ExpirationInfo>();
        var users = _userManager.GetUsers().ToList();

        if (config.MovieExpirationDays > 0)
            results.AddRange(GetMovieExpirations(config, users));

        if (config.SeasonExpirationDays > 0)
            results.AddRange(GetShowExpirations(config, users));

        return results.OrderBy(e => e.ExpiresAt).ToList();
    }

    /// <summary>
    /// Returns only items expiring within the configured window.
    /// </summary>
    public IReadOnlyList<ExpirationInfo> GetExpiringSoon()
    {
        var config = Plugin.Instance!.Configuration;
        var windowEnd = DateTime.UtcNow.AddDays(config.ExpiringSoonWindowDays);

        return GetExpirationCandidates()
            .Where(e => e.ExpiresAt <= windowEnd)
            .ToList();
    }

    /// <summary>
    /// Returns items that have already passed their expiration date.
    /// </summary>
    public IReadOnlyList<ExpirationInfo> GetExpired()
    {
        return GetExpirationCandidates()
            .Where(e => e.DaysRemaining < 0)
            .ToList();
    }

    /// <summary>
    /// Checks if disk space threshold condition is met (or disabled).
    /// </summary>
    public bool IsDiskSpaceThresholdMet()
    {
        var config = Plugin.Instance!.Configuration;
        if (config.DiskSpaceThresholdGb <= 0)
            return true; // No threshold = always allow expiration

        var monitorPath = string.IsNullOrEmpty(config.DiskSpaceMonitorPath)
            ? GetDefaultMediaPath()
            : config.DiskSpaceMonitorPath;

        if (string.IsNullOrEmpty(monitorPath) || !Directory.Exists(monitorPath))
        {
            _logger.LogWarning("Disk space monitor path not found: {Path}. Skipping expiration.", monitorPath);
            return false;
        }

        var driveInfo = new DriveInfo(monitorPath);
        var freeGb = driveInfo.AvailableFreeSpace / (1024L * 1024L * 1024L);

        _logger.LogInformation(
            "Disk check: {FreeGb}GB free on {Drive}, threshold is {ThresholdGb}GB",
            freeGb, driveInfo.Name, config.DiskSpaceThresholdGb);

        return freeGb < config.DiskSpaceThresholdGb;
    }

    // --- Private helpers ---

    private IEnumerable<ExpirationInfo> GetMovieExpirations(PluginConfiguration config, IReadOnlyList<User> users)
    {
        var movies = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie },
            IsVirtualItem = false
        }, false).OfType<Movie>();

        foreach (var movie in movies)
        {
            // Skip favorites (any user favoriting it protects the item)
            if (IsAnyUserFavorite(movie, users))
                continue;

            var lastWatched = GetLastWatchedAcrossAllUsers(movie, users);
            if (lastWatched is null)
            {
                if (config.UnwatchedMovieExpirationDays <= 0)
                    continue;
                var expiresAt = movie.DateCreated.AddDays(config.UnwatchedMovieExpirationDays);
                yield return new ExpirationInfo(movie, null, expiresAt, (int)(expiresAt - DateTime.UtcNow).TotalDays);
                continue;
            }

            {
                var expiresAt = lastWatched.Value.AddDays(config.MovieExpirationDays);
                yield return new ExpirationInfo(movie, lastWatched, expiresAt, (int)(expiresAt - DateTime.UtcNow).TotalDays);
            }
        }
    }

    /// <summary>
    /// Evaluates expiration at the show (Series) level rather than per-season: watching any
    /// episode in any season resets the clock for the entire show, and the whole show
    /// expires (and is deleted) together rather than season-by-season.
    /// </summary>
    private IEnumerable<ExpirationInfo> GetShowExpirations(PluginConfiguration config, IReadOnlyList<User> users)
    {
        var series = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Series },
            IsVirtualItem = false
        }, false).OfType<Series>().ToList();

        // TEMPORARY diagnostics for shows-missing-from-candidates issue
        _logger.LogInformation("GetShowExpirations: found {SeriesCount} series in library.", series.Count);

        foreach (var show in series)
        {
            // Skip if the show itself is favorited by anyone
            if (IsAnyUserFavorite(show, users))
            {
                _logger.LogInformation("GetShowExpirations: skipping '{Show}' — favorited.", show.Name);
                continue;
            }

            var seasons = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = show.Id,
                IncludeItemTypes = new[] { BaseItemKind.Season },
                IsVirtualItem = false
            }, false).OfType<Season>().ToList();

            // Skip if any individual season is favorited by anyone
            if (seasons.Any(season => IsAnyUserFavorite(season, users)))
            {
                _logger.LogInformation("GetShowExpirations: skipping '{Show}' — a season is favorited.", show.Name);
                continue;
            }

            // Gather every episode across every season of the show
            var episodes = new List<BaseItem>();
            foreach (var season in seasons)
            {
                episodes.AddRange(_libraryManager.GetItemList(new InternalItemsQuery
                {
                    ParentId = season.Id,
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    IsVirtualItem = false
                }, false));
            }

            _logger.LogInformation("GetShowExpirations: '{Show}' has {SeasonCount} seasons, {EpisodeCount} episodes.",
                show.Name, seasons.Count, episodes.Count);

            if (episodes.Count == 0)
            {
                _logger.LogInformation("GetShowExpirations: skipping '{Show}' — no episodes found.", show.Name);
                continue;
            }

            // Last watched = most recent watch across all episodes, all seasons, all users
            DateTime? lastWatched = null;
            foreach (var episode in episodes)
            {
                var episodeWatch = GetLastWatchedAcrossAllUsers(episode, users);
                if (episodeWatch.HasValue && (lastWatched is null || episodeWatch > lastWatched))
                    lastWatched = episodeWatch;
            }

            if (lastWatched is null)
            {
                if (config.UnwatchedSeasonExpirationDays <= 0)
                    continue;
                var expiresAt = show.DateCreated.AddDays(config.UnwatchedSeasonExpirationDays);
                yield return new ExpirationInfo(show, null, expiresAt, (int)(expiresAt - DateTime.UtcNow).TotalDays);
                continue;
            }

            {
                var expiresAt = lastWatched.Value.AddDays(config.SeasonExpirationDays);
                yield return new ExpirationInfo(show, lastWatched, expiresAt, (int)(expiresAt - DateTime.UtcNow).TotalDays);
            }
        }
    }

    private DateTime? GetLastWatchedAcrossAllUsers(BaseItem item, IReadOnlyList<User> users)
    {
        DateTime? latest = null;
        foreach (var user in users)
        {
            var userData = _userDataManager.GetUserData(user, item);
            if (userData?.LastPlayedDate.HasValue == true)
            {
                if (latest is null || userData.LastPlayedDate > latest)
                    latest = userData.LastPlayedDate;
            }
        }
        return latest;
    }

    private bool IsAnyUserFavorite(BaseItem item, IReadOnlyList<User> users)
    {
        return users.Any(user =>
        {
            var userData = _userDataManager.GetUserData(user, item);
            return userData?.IsFavorite == true;
        });
    }

    #nullable enable
    private string? GetDefaultMediaPath()
    #nullable restore
    {
        // Fall back to the first physical media folder found
        var mediaFolders = _libraryManager.GetVirtualFolders();
        foreach (var folder in mediaFolders)
        {
            var path = folder.Locations?.FirstOrDefault();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                return path;
        }
        return null;
    }
}