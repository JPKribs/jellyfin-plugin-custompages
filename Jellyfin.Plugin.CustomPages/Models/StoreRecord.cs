using System;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// One record in a <see cref="PageStore"/>. Everything except <see cref="Data"/> is assigned by the
/// server, so a page cannot forge an identity, a timestamp, or an owner on a record it writes.
/// </summary>
public class StoreRecord
{
    /// <summary>Gets or sets the server assigned record ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets when the record was created.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>Gets or sets when the record was last written.</summary>
    public DateTime UpdatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the ID of the user who created the record, or <c>null</c> when it was written
    /// anonymously. The creator is never reassigned by a later update, so ownership stays with whoever
    /// submitted the row.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the record's primary element, the one thing a person would call it. Supplied by the
    /// page and used to name the record in the activity log, so an administrator reads "Big Buck Bunny
    /// was added to downloads" rather than a bare record count. Optional, and never interpreted.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>Gets or sets the caller supplied payload. Its shape is entirely up to the page.</summary>
    public JsonNode? Data { get; set; }

    /// <summary>
    /// Returns an independent copy, so a record handed to a caller can never alias the instance the
    /// store keeps in memory behind its lock.
    /// </summary>
    /// <returns>A copy of this record.</returns>
    public StoreRecord Clone() => new StoreRecord
    {
        Id = Id,
        CreatedUtc = CreatedUtc,
        UpdatedUtc = UpdatedUtc,
        UserId = UserId,
        Label = Label,
        Data = Data?.DeepClone()
    };
}
