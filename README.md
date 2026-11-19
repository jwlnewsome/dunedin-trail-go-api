# DunedinTrails.Functions

.NET 10 isolated-worker Azure Functions API that replaces the Base44 backend for the Dunedin Trails app.
Data is held in an in-memory `EntityStore`, seeded from `Data/SeedData.cs`, and is reset whenever the host restarts.
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

Requires the .NET 10 SDK and Azure Functions Core Tools v4.

```powershell
cd src\DunedinTrails.Functions
func start --port 7248
```

Core Tools skips HTTP authorization by default. To enforce it locally, add `--enableAuth` to `func start`.

```powershell
dotnet test   # from the solution root
```

## Replacing the mock data

Swap `EntityStore` (registered as a singleton in `Program.cs`) for an implementation backed by a real database.
