using System.Text.Json;
using System.Text.Json.Nodes;
using DunedinTrails.Functions.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DunedinTrails.Functions.Functions;

/// <summary>
/// Generic CRUD over <see cref="IEntityStore"/>, replacing <c>base44.entities.{Entity}.*</c>.
/// </summary>
public sealed class EntityFunctions(IEntityStore store)
{
    public const int MaxLimit = 5000;

    [Function("ListEntities")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "entities/{entity}")] HttpRequest req, string entity,
        CancellationToken cancellationToken = default)
    {
        if (!EntityStore.IsKnownEntity(entity)) return UnknownEntity(entity);

        if (!TryReadInt(req, "limit", 1, MaxLimit, out var limit, out var error) ||
            !TryReadInt(req, "skip", 0, int.MaxValue, out var skip, out error))
        {
            return ApiJson.Error(StatusCodes.Status400BadRequest, error!);
        }

        JsonObject? filter = null;
        if (req.Query["q"].FirstOrDefault() is { Length: > 0 } q)
        {
            try
            {
                filter = JsonNode.Parse(q) as JsonObject
                         ?? throw new JsonException("q must be a JSON object.");
            }
            catch (JsonException ex)
            {
                return ApiJson.Error(StatusCodes.Status400BadRequest, $"Invalid q filter: {ex.Message}");
            }
        }

        try
        {
            return ApiJson.Ok(await store.ListAsync(entity, filter, req.Query["sort"].FirstOrDefault(), limit, skip ?? 0, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return ApiJson.Error(StatusCodes.Status400BadRequest, ex.Message);
        }
    }

    [Function("GetEntity")]
    public async Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "entities/{entity}/{id}")] HttpRequest req,
        string entity, string id, CancellationToken cancellationToken = default)
    {
        if (!EntityStore.IsKnownEntity(entity)) return UnknownEntity(entity);
        return await store.GetAsync(entity, id, cancellationToken) is { } record ? ApiJson.Ok(record) : NotFound(entity, id);
    }

    [Function("CreateEntity")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "entities/{entity}")] HttpRequest req,
        string entity, CancellationToken cancellationToken)
    {
        if (!EntityStore.IsKnownEntity(entity)) return UnknownEntity(entity);
        if (await ReadBodyAsync(req, cancellationToken) is not JsonObject data)
        {
            return ApiJson.Error(StatusCodes.Status400BadRequest, "Request body must be a JSON object.");
        }
        return ApiJson.Ok(await store.CreateAsync(entity, data, SeedData.AdminEmail, cancellationToken), StatusCodes.Status201Created);
    }

    [Function("BulkCreateEntities")]
    public async Task<IActionResult> BulkCreate(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "entities/{entity}/bulk")] HttpRequest req,
        string entity, CancellationToken cancellationToken)
    {
        if (!EntityStore.IsKnownEntity(entity)) return UnknownEntity(entity);
        if (await ReadBodyAsync(req, cancellationToken) is not JsonArray items || items.Any(i => i is not JsonObject))
        {
            return ApiJson.Error(StatusCodes.Status400BadRequest, "Request body must be a JSON array of objects.");
        }
        var created = new List<JsonObject>();
        foreach (var item in items)
            created.Add(await store.CreateAsync(entity, item!.AsObject(), SeedData.AdminEmail, cancellationToken));
        return ApiJson.Ok(created, StatusCodes.Status201Created);
    }

    [Function("UpdateEntity")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Function, "put", "patch", Route = "entities/{entity}/{id}")] HttpRequest req,
        string entity, string id, CancellationToken cancellationToken)
    {
        if (!EntityStore.IsKnownEntity(entity)) return UnknownEntity(entity);
        if (await ReadBodyAsync(req, cancellationToken) is not JsonObject patch)
        {
            return ApiJson.Error(StatusCodes.Status400BadRequest, "Request body must be a JSON object.");
        }
        return await store.UpdateAsync(entity, id, patch, cancellationToken) is { } record ? ApiJson.Ok(record) : NotFound(entity, id);
    }

    [Function("DeleteEntity")]
    public async Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "entities/{entity}/{id}")] HttpRequest req,
        string entity, string id, CancellationToken cancellationToken = default)
    {
        if (!EntityStore.IsKnownEntity(entity)) return UnknownEntity(entity);
        return await store.DeleteAsync(entity, id, cancellationToken) ? ApiJson.Ok(new { success = true }) : NotFound(entity, id);
    }

    internal static async Task<JsonNode?> ReadBodyAsync(HttpRequest req, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonNode.ParseAsync(req.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadInt(HttpRequest req, string name, int min, int max, out int? value, out string? error)
    {
        value = null;
        error = null;
        if (req.Query[name].FirstOrDefault() is not { Length: > 0 } raw) return true;
        if (int.TryParse(raw, out var parsed) && parsed >= min && parsed <= max)
        {
            value = parsed;
            return true;
        }
        error = $"{name} must be an integer between {min} and {max}.";
        return false;
    }

    private static JsonResult UnknownEntity(string entity) =>
        ApiJson.Error(StatusCodes.Status404NotFound, $"Unknown entity '{entity}'.");

    private static JsonResult NotFound(string entity, string id) =>
        ApiJson.Error(StatusCodes.Status404NotFound, $"{entity} '{id}' not found.");
}
