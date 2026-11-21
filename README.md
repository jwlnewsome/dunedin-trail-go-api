# DunedinTrails.Functions

.NET 10 isolated-worker Azure Functions API that replaces the Base44 backend for the Dunedin Trails app.
Data is stored in Azure Cosmos DB for NoSQL and persists across host restarts.
All JSON uses snake_case.

## Endpoints

| Method          | Route                          | Notes                                                                 |
|-----------------|--------------------------------|-----------------------------------------------------------------------|
| GET             | `/api/trails`                  | Requires a valid access key. `sort` (default `-created_date`), `limit` 1–1000 |
| GET             | `/api/entities/{entity}`       | `sort` (`-` = desc), `limit` 1–5000, `skip`, `q` (JSON filter: equality, `$in`, `$nin`, `$ne`) |
| POST            | `/api/entities/{entity}`       | Create (returns 201)                                                  |
| POST            | `/api/entities/{entity}/bulk`  | Bulk create (JSON array)                                              |
| GET             | `/api/entities/{entity}/{id}`  | Get by id                                                             |
| PUT/PATCH       | `/api/entities/{entity}/{id}`  | Merge update                                                          |
| DELETE          | `/api/entities/{entity}/{id}`  | Delete                                                                |
| GET / PUT/PATCH | `/api/auth/me`                 | Mock admin user; `email` and `role` can't be changed                  |
| POST            | `/api/functions/{name}`        | `getDocAlertGroups`, `getDccNotices`, `deleteMyAccount` (mocked)      |

Unknown entities return `404`. Invalid input returns `400` with `{ "error": "..." }`.

For `GET /api/trails`, trusted server-side callers should send the function-level `default` key for `GetTrailsForMap` in the `x-functions-key` header over HTTPS. Use the key listed under **Function Keys** for `GetTrailsForMap`, not the app-wide `default` host key. Never put the key in browser or mobile code.

## Run locally

Requires the .NET 10 SDK, Azure Functions Core Tools v4, and Cosmos DB data-plane access.

Configure these Function App settings (or add them to the ignored `local.settings.json` under `Values`):

| Setting | Value |
|---------|-------|
| `CosmosDb__Endpoint` | Your Cosmos DB account endpoint, e.g. `https://dun-tr-dev-cos.documents.azure.com:443/` |
| `CosmosDb__Database` | `dunedintracks` |

Use `az login` locally and enable a managed identity for the Function App in Azure. Assign the
identity a Cosmos DB **data-plane** role with read, create, replace, delete and query permissions
on the database; Azure management roles alone do not grant data access. No account keys are
needed. These settings are required: the API will not fall back to in-memory data.

The database must already contain `catalog` (partition key `/entity_type`) and `user_data`
(partition key `/user_id`). Seed records can be imported separately; startup does not re-seed
or overwrite existing records. Listing retains the existing arbitrary filter, sort and `skip`
behavior by reading all matching entity-type pages before applying those operations in memory,
so large collections will consume more RUs and memory.

Authentication remains a **mock**: every caller is the same admin user. `TrailCompletion`,
`TrailWishlist`, and `UserRoute` records are placed in that mock user's partition; `User` records
use their own IDs as partition keys. This is not suitable for real multi-user data until
authentication, ownership enforcement and user-partition routing are implemented.

```powershell
cd src\DunedinTrails.Functions
func start --port 7248
```

Core Tools skips HTTP authorization by default. To enforce it locally, add `--enableAuth` to `func start`.

```powershell
dotnet test   # from the solution root
```

The DOC and DCC alert functions still return mocked empty responses.
