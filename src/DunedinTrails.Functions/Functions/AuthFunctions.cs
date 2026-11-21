using System.Text.Json.Nodes;
using DunedinTrails.Functions.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DunedinTrails.Functions.Functions;

/// <summary>
/// Mock replacement for <c>base44.auth</c>: every caller is the seeded admin user.
/// </summary>
public sealed class AuthFunctions(IEntityStore store)
{
    [Function("GetMe")]
    public async Task<IActionResult> Me(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "auth/me")] HttpRequest req,
        CancellationToken cancellationToken = default) =>
        await store.GetAsync("User", SeedData.AdminUserId, cancellationToken) is { } user
            ? ApiJson.Ok(user)
            : ApiJson.Error(StatusCodes.Status401Unauthorized, "Not signed in.");

    [Function("UpdateMe")]
    public async Task<IActionResult> UpdateMe(
        [HttpTrigger(AuthorizationLevel.Function, "put", "patch", Route = "auth/me")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        if (await EntityFunctions.ReadBodyAsync(req, cancellationToken) is not JsonObject patch)
        {
            return ApiJson.Error(StatusCodes.Status400BadRequest, "Request body must be a JSON object.");
        }

        // Identity fields can't be changed through updateMe (matches Base44).
        foreach (var field in new[] { "email", "role" }) patch.Remove(field);

        return await store.UpdateAsync("User", SeedData.AdminUserId, patch, cancellationToken) is { } user
            ? ApiJson.Ok(user)
            : ApiJson.Error(StatusCodes.Status401Unauthorized, "Not signed in.");
    }
}
