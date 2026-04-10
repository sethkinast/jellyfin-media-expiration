using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaExpiration.Providers;
using MediaBrowser.Controller.Collections;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaExpiration.ScheduledTasks;

public class ExpiringSoonCollectionTask : IScheduledTask
{
    private const string CollectionName = Plugin.ExpiringSoonCollectionName;

    private readonly ICollectionManager _collectionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ExpirationManager _expirationManager;
    private readonly ILogger<ExpiringSoonCollectionTask> _logger;

    public string Name => "Update Expiring Soon Collection";
    public string Key => "MediaExpirationExpiringSoon";
    public string Description => "Refreshes the 'Expiring Soon' collection on the home screen.";
    public string Category => "Media Expiration";

    public ExpiringSoonCollectionTask(
        ICollectionManager collectionManager,
        ILibraryManager libraryManager,
        ExpirationManager expirationManager,
        ILogger<ExpiringSoonCollectionTask> logger)
    {
        _collectionManager = collectionManager;
        _libraryManager = libraryManager;
        _expirationManager = expirationManager;
        _logger = logger;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(5).Ticks
        }
    };

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var expiringSoon = _expirationManager.GetExpiringSoon().ToList();

        // Find or create the collection
        var existing = _libraryManager.GetItemList(new InternalItemsQuery
        {
            Name = CollectionName,
            IncludeItemTypes = new[] { BaseItemKind.BoxSet }
        }, false).FirstOrDefault();

        if (existing is null)
        {
            _logger.LogInformation("Creating '{CollectionName}' collection.", CollectionName);
            existing = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = CollectionName
            }).ConfigureAwait(false);
        }

        // Replace collection contents — LinkedChildren holds the raw links without needing a User
        var currentIds = ((Folder)existing).LinkedChildren
            .Where(lc => lc.ItemId.HasValue)
            .Select(lc => lc.ItemId!.Value)
            .ToHashSet();

        var desiredIds = expiringSoon
            .Select(e => e.Item.Id)
            .ToHashSet();

        var toRemove = currentIds.Except(desiredIds).ToList();
        var toAdd = desiredIds.Except(currentIds).ToList();

        if (toRemove.Count > 0)
            await _collectionManager.RemoveFromCollectionAsync(existing.Id, toRemove).ConfigureAwait(false);

        if (toAdd.Count > 0)
            await _collectionManager.AddToCollectionAsync(existing.Id, toAdd).ConfigureAwait(false);

        _logger.LogInformation(
            "Expiring Soon collection updated: {Added} added, {Removed} removed, {Total} total.",
            toAdd.Count, toRemove.Count, expiringSoon.Count);

        progress.Report(100);
    }
}