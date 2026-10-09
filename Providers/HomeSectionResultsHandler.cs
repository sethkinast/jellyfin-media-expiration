#nullable enable
using System;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;

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

    public HomeSectionResultsHandler(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IDtoService dtoService)
    {
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _userManager = userManager;
    }

    public QueryResult<BaseItemDto> GetResults(HomeSectionRequest request)
    {
        var collection = _libraryManager.GetItemList(new InternalItemsQuery
        {
            Name = CollectionName,
            IncludeItemTypes = new[] { BaseItemKind.BoxSet }
        }, false).FirstOrDefault() as Folder;

        if (collection is null)
            return new QueryResult<BaseItemDto>();

        var items = collection.LinkedChildren
            .Where(lc => lc.ItemId.HasValue)
            .Select(lc => _libraryManager.GetItemById(lc.ItemId!.Value))
            .Where(item => item is not null)
            .Cast<BaseItem>()
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        var user = _userManager.GetUserById(request.UserId);
        var dtos = _dtoService.GetBaseItemDtos(items, new DtoOptions(), user);

        return new QueryResult<BaseItemDto>
        {
            Items = dtos,
            TotalRecordCount = dtos.Count
        };
    }
}
