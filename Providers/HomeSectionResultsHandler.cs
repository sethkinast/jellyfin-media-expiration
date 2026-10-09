#nullable enable
using System;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaExpiration.Providers;

public class HomeSectionRequest
{
    public Guid UserId { get; set; }
    public string? AdditionalData { get; set; }
}

public class HomeSectionResultsHandler
{
    private const string CollectionName = Plugin.ExpiringSoonCollectionName;

    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly IUserManager _userManager;
    private readonly ILogger<HomeSectionResultsHandler> _logger;

    public HomeSectionResultsHandler(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IDtoService dtoService,
        ILogger<HomeSectionResultsHandler> logger)
    {
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _userManager = userManager;
        _logger = logger;
    }

    public QueryResult<BaseItemDto> GetResults(HomeSectionRequest request)
    {
        // TEMPORARY diagnostic logging for the home-section-not-rendering issue.
        try
        {
            _logger.LogInformation("HomeSectionResultsHandler.GetResults called for user {UserId}.", request.UserId);

            var collection = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Name = CollectionName,
                IncludeItemTypes = new[] { BaseItemKind.BoxSet }
            }, false).FirstOrDefault() as Folder;

            if (collection is null)
            {
                _logger.LogWarning("GetResults: '{CollectionName}' collection not found.", CollectionName);
                return new QueryResult<BaseItemDto>();
            }

            _logger.LogInformation("GetResults: found collection '{CollectionName}' ({CollectionId}) with {LinkedCount} linked children.",
                CollectionName, collection.Id, collection.LinkedChildren.Length);

            var items = collection.LinkedChildren
                .Where(lc => lc.ItemId.HasValue)
                .Select(lc => _libraryManager.GetItemById(lc.ItemId!.Value))
                .Where(item => item is not null)
                .Cast<BaseItem>()
                .OrderBy(_ => Random.Shared.Next())
                .ToList();

            _logger.LogInformation("GetResults: resolved {ItemCount} items from linked children.", items.Count);

            var user = _userManager.GetUserById(request.UserId);
            _logger.LogInformation("GetResults: user lookup for {UserId} -> {UserResult}.", request.UserId, user?.Username ?? "<null>");

            var dtos = _dtoService.GetBaseItemDtos(items, new DtoOptions(), user);
            _logger.LogInformation("GetResults: built {DtoCount} DTOs.", dtos.Count);

            return new QueryResult<BaseItemDto>
            {
                Items = dtos,
                TotalRecordCount = dtos.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetResults: exception while building home section results.");
            throw;
        }
    }
}
