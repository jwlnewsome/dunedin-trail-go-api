using DunedinTrails.Functions.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DunedinTrails.Functions.Functions;

/// <summary>
/// Mock replacements for the Base44 backend functions invoked by the frontend via <c>functions.invoke</c>.
/// </summary>
public sealed class BackendFunctions(IEntityStore store)
{
    [Function("InvokeBackendFunction")]
    public async Task<IActionResult> Invoke(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "functions/{name}")] HttpRequest req, string name,
        CancellationToken cancellationToken = default) =>
        name switch
        {
            // Real version scrapes the DOC alerts API; no external calls in the mock.
            "getDocAlertGroups" => ApiJson.Ok(new { groups = Array.Empty<object>(), region = "Otago", guid = (string?)null }),
            "getDccNotices" => ApiJson.Ok(new { notices = Array.Empty<object>() }),
            "deleteMyAccount" => await DeleteMyAccount(cancellationToken),
            _ => ApiJson.Error(StatusCodes.Status404NotFound, $"Unknown function '{name}'."),
        };

    // Removes the mock user's personal records but keeps the account so the app stays usable.
    private async Task<JsonResult> DeleteMyAccount(CancellationToken cancellationToken)
    {
        foreach (var entity in new[] { "TrailCompletion", "TrailWishlist", "UserRoute" })
        {
            foreach (var record in await store.ListAsync(entity, new() { ["user_email"] = SeedData.AdminEmail }, cancellationToken: cancellationToken))
            {
                await store.DeleteAsync(entity, record["id"]!.GetValue<string>(), cancellationToken);
            }
        }
        return ApiJson.Ok(new { success = true });
    }
}
