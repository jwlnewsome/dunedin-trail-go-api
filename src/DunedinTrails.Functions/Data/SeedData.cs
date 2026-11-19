using System.Text.Json;
using System.Text.Json.Nodes;

namespace DunedinTrails.Functions.Data;

public static class SeedData
{
    public const string AdminUserId = "user-admin";
    public const string AdminEmail = "admin@example.com";

    internal static IEnumerable<(string Entity, JsonObject Record)> Create(DateTimeOffset now)
    {
        var seeded = now.AddDays(-30).ToString("O");

        foreach (var node in JsonSerializer.SerializeToNode(MockTrails.All, ApiJson.Options)!.AsArray())
        {
            yield return ("Trail", node!.AsObject());
        }

        yield return ("User", Parse($$"""
            {
              "id": "{{AdminUserId}}", "email": "{{AdminEmail}}", "full_name": "Mock Admin", "role": "admin",
              "disclaimer_accepted": true, "disclaimer_version": "1.0", "disclaimer_accepted_at": "{{seeded}}",
              "membership_status": "active", "created_date": "{{seeded}}", "updated_date": "{{seeded}}"
            }
            """));

        yield return ("LocationHub", Hub("hub-signal-hill", "Signal Hill", "general", -45.8506, 170.5300,
            "Lookout and trail network above Opoho with views over the city and harbour.", seeded));
        yield return ("LocationHub", Hub("hub-otago-peninsula", "Otago Peninsula", "general", -45.8900, 170.6400,
            "Coastal walks, beaches and wildlife viewing on the Otago Peninsula.", seeded));
        yield return ("LocationHub", Hub("hub-mt-cargill-mtb", "Mt Cargill MTB Park", "MTB", -45.8155, 170.5560,
            "Downhill and cross-country mountain bike trails on Mt Cargill.", seeded));

        yield return ("Group", Parse($$"""
            {
              "id": "group-dunedin-ramblers", "name": "Dunedin Ramblers", "status": "active",
              "activity_type": "walking", "activity_types": ["walking"], "skill_level": "all_levels",
              "description": "Friendly weekend walking group exploring tracks around Dunedin.",
              "meeting_schedule": "Saturdays 9am", "meeting_location": "Octagon", "member_count": 42,
              "created_date": "{{seeded}}", "updated_date": "{{seeded}}", "created_by": "{{AdminEmail}}"
            }
            """));
        yield return ("Group", Parse($$"""
            {
              "id": "group-dunedin-mtb", "name": "Dunedin MTB Club", "status": "active",
              "activity_type": "biking", "activity_types": ["biking"], "skill_level": "intermediate",
              "description": "Club rides and trail-building days on Mt Cargill and Signal Hill.",
              "meeting_schedule": "Wednesdays 6pm", "meeting_location": "Bethunes Gully car park", "member_count": 120,
              "created_date": "{{seeded}}", "updated_date": "{{seeded}}", "created_by": "{{AdminEmail}}"
            }
            """));

        // Event dates are relative to start-up so there are always upcoming events.
        yield return ("Event", Event("event-peninsula-walk", "Peninsula Sunrise Walk", now.AddDays(7), "Sandfly Bay car park",
            -45.8957, 170.6420, "walking", "group-dunedin-ramblers", seeded));
        yield return ("Event", Event("event-cargill-enduro", "Mt Cargill Enduro", now.AddDays(21), "Bethunes Gully",
            -45.8155, 170.5560, "biking", "group-dunedin-mtb", seeded));
    }

    private static JsonObject Hub(string id, string name, string hubType, double lat, double lng, string description, string seeded) =>
        new()
        {
            ["id"] = id, ["name"] = name, ["hub_type"] = hubType, ["status"] = "active",
            ["latitude"] = lat, ["longitude"] = lng, ["description"] = description,
            ["facilities"] = new JsonArray("Parking", "Toilets"),
            ["created_date"] = seeded, ["updated_date"] = seeded, ["created_by"] = AdminEmail,
        };

    private static JsonObject Event(string id, string title, DateTimeOffset date, string location, double lat, double lng,
        string activity, string groupId, string seeded) =>
        new()
        {
            ["id"] = id, ["title"] = title, ["status"] = "active",
            ["event_date"] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "T09:00:00",
            ["location"] = location,
            ["latitude"] = lat, ["longitude"] = lng, ["activity_type"] = activity,
            ["activity_types"] = new JsonArray(activity), ["difficulty"] = "all_levels", ["cost"] = "Free",
            ["group_id"] = groupId, ["description"] = $"Join us for the {title}.",
            ["created_date"] = seeded, ["updated_date"] = seeded, ["created_by"] = AdminEmail,
        };

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
}
