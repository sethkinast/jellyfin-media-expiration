using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.MediaExpiration.Configuration;
using Jellyfin.Data.Enums;
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

        if (config.MovieExpirationDays > 0)
            results.AddRange(GetMovieExpirations(config));

        if (config.SeasonExpirationDays > 0)
            results.AddRange(GetSeasonExpirations(config));

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

    private IEnumerable<ExpirationInfo> GetMovieExpirations(PluginConfiguration config)
    {
        var movies = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie },
            IsVirtualItem = false
        }, false).OfType<Movie>();

        foreach (var movie in movies)
        {
            // Skip favorites (any user favoriting it protects the item)
            if (IsAnyUserFavorite(movie))
                continue;

            var lastWatched = GetLastWatchedAcrossAllUsers(movie);
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

    private IEnumerable<ExpirationInfo> GetSeasonExpirations(PluginConfiguration config)
    {
        var seasons = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Season },
            IsVirtualItem = false
        }, false).OfType<Season>();

        foreach (var season in seasons.OfType<Season>())
        {
            // Skip if the season or its parent show is favorited by anyone
            if (IsAnyUserFavorite(season) || (season.Series is not null && IsAnyUserFavorite(season.Series)))
                continue;

            // Get all episodes in this season
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = season.Id,
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsVirtualItem = false
            }, false);

            // Last watched = most recent watch across all episodes, all users
            DateTime? lastWatched = null;
            foreach (var episode in episodes)
            {
                var episodeWatch = GetLastWatchedAcrossAllUsers(episode);
                if (episodeWatch.HasValue && (lastWatched is null || episodeWatch > lastWatched))
                    lastWatched = episodeWatch;
            }

            if (lastWatched is null)
            {
                if (config.UnwatchedSeasonExpirationDays <= 0)
                    continue;
                var expiresAt = season.DateCreated.AddDays(config.UnwatchedSeasonExpirationDays);
                yield return new ExpirationInfo(season, null, expiresAt, (int)(expiresAt - DateTime.UtcNow).TotalDays);
                continue;
            }

            {
                var expiresAt = lastWatched.Value.AddDays(config.SeasonExpirationDays);
                yield return new ExpirationInfo(season, lastWatched, expiresAt, (int)(expiresAt - DateTime.UtcNow).TotalDays);
            }
        }
    }

    private DateTime? GetLastWatchedAcrossAllUsers(BaseItem item)
    {
        DateTime? latest = null;
        foreach (var user in _userManager.Users)
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

    private bool IsAnyUserFavorite(BaseItem item)
    {
        return _userManager.Users.Any(user =>
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