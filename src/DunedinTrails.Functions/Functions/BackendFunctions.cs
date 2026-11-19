using DunedinTrails.Functions.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DunedinTrails.Functions.Functions;

/// <summary>
/// Mock replacements for the Base44 backend functions invoked by the frontend via <c>functions.invoke</c>.
/// </summary>
public sealed class BackendFunctions(EntityStore store)
{
    [Function("InvokeBackendFunction")]
    public IActionResult Invoke(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "functions/{name}")] HttpRequest req, string name) =>
        name switch
        {
            // Real version scrapes the DOC alerts API; no external calls in the mock.
            "getDocAlertGroups" => ApiJson.Ok(new { groups = Array.Empty<object>(), region = "Otago", guid = (string?)null }),
            "getDccNotices" => ApiJson.Ok(new { notices = Array.Empty<object>() }),
            "deleteMyAccount" => DeleteMyAccount(),
            _ => ApiJson.Error(StatusCodes.Status404NotFound, $"Unknown function '{name}'."),
        };

    // Removes the mock user's personal records but keeps the account so the app stays usable.
    private JsonResult DeleteMyAccount()
    {
        foreach (var entity in new[] { "TrailCompletion", "TrailWishlist", "UserRoute" })
        {
            foreach (var record in store.List(entity, new() { ["user_email"] = SeedData.AdminEmail }))
            {
                store.Delete(entity, record["id"]!.GetValue<string>());
            }
        }
        return ApiJson.Ok(new { success = true });
    }
}
