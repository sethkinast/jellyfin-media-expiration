using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MediaExpiration.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Days after last watch before a movie expires. 0 = disabled.</summary>
    public int MovieExpirationDays { get; set; } = 180;

    /// <summary>Days after last watch before a TV season expires. 0 = disabled.</summary>
    public int SeasonExpirationDays { get; set; } = 90;

    /// <summary>Days after DateCreated before an unwatched movie expires. 0 = disabled.</summary>
    public int UnwatchedMovieExpirationDays { get; set; } = 0;

    /// <summary>Days after DateCreated before an unwatched TV season expires. 0 = disabled.</summary>
    public int UnwatchedSeasonExpirationDays { get; set; } = 0;

    /// <summary>Items expiring within this many days appear in the Expiring Soon collection.</summary>
    public int ExpiringSoonWindowDays { get; set; } = 14;

    /// <summary>
    /// Expiration only activates when free disk space (in GB) drops below this threshold.
    /// 0 means always expire regardless of disk space.
    /// </summary>
    public long DiskSpaceThresholdGb { get; set; } = 0;

    /// <summary>Path to monitor for disk space. Defaults to media library path if empty.</summary>
    public string DiskSpaceMonitorPath { get; set; } = string.Empty;

    /// <summary>Hour of day (0-23) to run the expiration task.</summary>
    public int ScheduledTaskHour { get; set; } = 3;

    /// <summary>If true, media will not be deleted; only logs will be emitted.</summary>
    public bool DryRun { get; set; } = true;
}