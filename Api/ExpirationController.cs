using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.MediaExpiration.Configuration;
using Jellyfin.Plugin.MediaExpiration.Providers;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MediaExpiration.Api;

[ApiController]
[Route("MediaExpiration")]
public class ExpirationController : ControllerBase
{
    private readonly ExpirationManager _expirationManager;
    private readonly ILibraryManager _libraryManager;

    public ExpirationController(ExpirationManager expirationManager, ILibraryManager libraryManager)
    {
        _expirationManager = expirationManager;
        _libraryManager = libraryManager;
    }

    /// <summary>Get expiration info for a specific item (used by detail page JS).</summary>
    [HttpGet("Item/{itemId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ExpirationInfoDto> GetItemExpiration(string itemId)
    {
        var candidates = _expirationManager.GetExpirationCandidates();
        var match = candidates.FirstOrDefault(e => e.Item.Id.ToString("N") == itemId);

        // If no direct match, check if this is an episode and inherit from its season
        if (match is null)
        {
            var item = _libraryManager.GetItemById(Guid.ParseExact(itemId, "N"));
            if (item is Episode episode && episode.Season is not null)
            {
                var seasonId = episode.Season.Id.ToString("N");
                match = candidates.FirstOrDefault(e => e.Item.Id.ToString("N") == seasonId);
            }
        }

        if (match is null)
            return NotFound();

        return Ok(new ExpirationInfoDto(
            match.Item.Id,
            match.LastWatched,
            match.ExpiresAt,
            match.DaysRemaining));
    }

    /// <summary>Get all items expiring soon.</summary>
    [HttpGet("ExpiringSoon")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<ExpirationInfoDto>> GetExpiringSoon()
    {
        return Ok(_expirationManager.GetExpiringSoon()
            .Select(e => new ExpirationInfoDto(e.Item.Id, e.LastWatched, e.ExpiresAt, e.DaysRemaining)));
    }
}

public record ExpirationInfoDto(
    [property: JsonPropertyName("itemId")] Guid ItemId,
    [property: JsonPropertyName("lastWatched")] DateTime? LastWatched,
    [property: JsonPropertyName("expiresAt")] DateTime ExpiresAt,
    [property: JsonPropertyName("daysRemaining")] int DaysRemaining);