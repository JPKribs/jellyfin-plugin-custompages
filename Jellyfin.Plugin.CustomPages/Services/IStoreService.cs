using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.CustomPages.Models;

namespace Jellyfin.Plugin.CustomPages.Services;

/// <summary>
/// The outcome of a store write, so the controller can answer with the right status without repeating
/// the store's own limits.
/// </summary>
public enum StoreWriteResult
{
    /// <summary>The record was created or updated.</summary>
    Ok,

    /// <summary>The record ID named by an update does not exist, or is not in the caller's scope.</summary>
    NotFound,

    /// <summary>The store already holds its configured maximum number of records.</summary>
    Full,

    /// <summary>The supplied payload is larger than a single record may be.</summary>
    TooLarge,

    /// <summary>The records could not be written to disk.</summary>
    Failed
}

/// <summary>
/// Reads and writes the record collections backing <c>/pages/store/{name}</c>. Exists as an interface so
/// the controller, where the tiers are enforced, can be tested without touching the filesystem.
/// </summary>
public interface IStoreService
{
    /// <summary>
    /// Finds an enabled store definition by name, case-insensitively.
    /// </summary>
    /// <param name="name">The store name.</param>
    /// <returns>The matching store, or <c>null</c> when none is enabled for the name.</returns>
    PageStore? Find(string name);

    /// <summary>
    /// Returns the records in a store, newest first.
    /// </summary>
    /// <param name="store">The store to read.</param>
    /// <param name="owner">When set, only records created by this user are returned.</param>
    /// <returns>The matching records.</returns>
    IReadOnlyList<StoreRecord> Read(PageStore store, Guid? owner);

    /// <summary>
    /// Returns one record by ID.
    /// </summary>
    /// <param name="store">The store to read.</param>
    /// <param name="id">The record ID.</param>
    /// <param name="owner">When set, the record is only returned if this user created it.</param>
    /// <returns>The record, or <c>null</c> when it does not exist or is out of scope.</returns>
    StoreRecord? ReadOne(PageStore store, string id, Guid? owner);

    /// <summary>
    /// Creates a record owned by the caller.
    /// </summary>
    /// <param name="store">The store to write to.</param>
    /// <param name="data">The caller supplied payload.</param>
    /// <param name="label">The record's primary element, used to name it in the activity log.</param>
    /// <param name="userId">The creating user, or <see cref="Guid.Empty"/> for an anonymous write.</param>
    /// <returns>The outcome, and the stored record when it succeeded.</returns>
    (StoreWriteResult Result, StoreRecord? Record) Create(PageStore store, JsonNode? data, string? label, Guid userId);

    /// <summary>
    /// Replaces an existing record's payload, leaving its ID, creation time, and owner intact.
    /// </summary>
    /// <param name="store">The store to write to.</param>
    /// <param name="id">The record ID.</param>
    /// <param name="data">The replacement payload.</param>
    /// <param name="label">The replacement label, or <c>null</c> to keep the one the record carries.</param>
    /// <param name="owner">When set, the record is only updated if this user created it.</param>
    /// <returns>The outcome, and the stored record when it succeeded.</returns>
    (StoreWriteResult Result, StoreRecord? Record) Update(PageStore store, string id, JsonNode? data, string? label, Guid? owner);

    /// <summary>
    /// Deletes one record.
    /// </summary>
    /// <param name="store">The store to write to.</param>
    /// <param name="id">The record ID.</param>
    /// <param name="owner">When set, the record is only deleted if this user created it.</param>
    /// <returns><c>true</c> when a record was deleted.</returns>
    bool Delete(PageStore store, string id, Guid? owner);

    /// <summary>
    /// Deletes every record in a store. Used by the dashboard, never reachable from a page.
    /// </summary>
    /// <param name="store">The store to empty.</param>
    /// <returns>The number of records removed.</returns>
    int Clear(PageStore store);

    /// <summary>
    /// Returns how many records a store currently holds.
    /// </summary>
    /// <param name="store">The store to measure.</param>
    /// <returns>The record count.</returns>
    int Count(PageStore store);

    /// <summary>
    /// Applies every store's retention policy, for the scheduled task.
    /// </summary>
    /// <returns>The number of records removed across all stores.</returns>
    int SweepAll();
}
