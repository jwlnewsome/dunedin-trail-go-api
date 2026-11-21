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

## Fresh Azure installation

These steps configure Cosmos DB for a newly provisioned Function App. First create and deploy
an Azure Functions v4, .NET 10 isolated-worker Function App using your deployment workflow.
The commands below do not provision the Function App, its hosting plan, or its storage account.
Run them in PowerShell with Azure CLI installed. The signed-in operator must be allowed to
create Cosmos resources, update the Function App, and create Cosmos DB SQL role assignments.
Creating cloud resources incurs charges; this example uses single-region serverless Cosmos DB.

### 1. Select the target and create the database

Replace the example names before running. Cosmos account names must be globally unique.
If the resource group or Cosmos account already exists, skip its creation command.

```powershell
az login
$subscription = "<subscription-id>"
$resourceGroup = "dunedin-trails"
$location = "australiaeast"
$functionApp = "<your-function-app-name>"
$cosmosAccount = "<globally-unique-cosmos-account-name>"
$database = "dunedintracks"

az account set --subscription $subscription
az group create --name $resourceGroup --location $location --output none
az cosmosdb create --name $cosmosAccount --resource-group $resourceGroup `
  --kind GlobalDocumentDB --capabilities EnableServerless `
  --locations regionName=$location failoverPriority=0 --output none
az cosmosdb sql database create --account-name $cosmosAccount `
  --resource-group $resourceGroup --name $database --output none
az cosmosdb sql container create --account-name $cosmosAccount `
  --resource-group $resourceGroup --database-name $database `
  --name catalog --partition-key-path /entity_type --output none
az cosmosdb sql container create --account-name $cosmosAccount `
  --resource-group $resourceGroup --database-name $database `
  --name user_data --partition-key-path /user_id --output none
```

Do not specify provisioned throughput for a serverless account. Alternatively, create these
containers using the VS Code Cosmos DB extension, with exactly the partition keys above.
Shared entities belong in `catalog`, with `entity_type` matching the API entity name.
`User`, `TrailCompletion`, `TrailWishlist`, and `UserRoute` belong in `user_data`.

### 2. Enable the Function App identity and grant database access

```powershell
$principalId = az functionapp identity assign --name $functionApp `
  --resource-group $resourceGroup --query principalId --output tsv
if ($LASTEXITCODE -ne 0 -or !$principalId) {
  throw "Could not enable the Function App managed identity."
}

az cosmosdb sql role assignment create --account-name $cosmosAccount `
  --resource-group $resourceGroup --principal-id $principalId `
  --role-definition-id 00000000-0000-0000-0000-000000000002 `
  --scope "/dbs/$database" --output none
```

Role `00000000-0000-0000-0000-000000000002` is **Cosmos DB Built-in Data Contributor**.
It allows document reads, queries and writes, scoped here to this database rather than the
whole account. It is a **Cosmos DB data-plane assignment**, not an Azure IAM assignment:
it does not appear in the managed identity's **Azure role assignments** view. Verify it with:

```powershell
az cosmosdb sql role assignment list --account-name $cosmosAccount `
  --resource-group $resourceGroup --output table
```

Match the assignment's `principalId` to `$principalId` and its scope to `/dbs/$database`
(Azure may return the fully qualified account resource ID followed by that path).
Allow time for identity and role changes to propagate. Do not grant the application an
Azure IAM Contributor role just to access documents.

Local development uses your own `az login` identity, not the Function App's identity.
If you also need local read/write access, have an authorised operator grant your user the
same database-scoped data-plane role:

```powershell
$developerId = az ad signed-in-user show --query id --output tsv
if ($LASTEXITCODE -ne 0 -or !$developerId) {
  throw "Could not resolve the signed-in developer."
}
az cosmosdb sql role assignment create --account-name $cosmosAccount `
  --resource-group $resourceGroup --principal-id $developerId `
  --role-definition-id 00000000-0000-0000-0000-000000000002 `
  --scope "/dbs/$database" --output none
```

### 3. Configure the Function App

```powershell
$endpoint = az cosmosdb show --name $cosmosAccount --resource-group $resourceGroup `
  --query documentEndpoint --output tsv
if ($LASTEXITCODE -ne 0 -or !$endpoint) {
  throw "Could not resolve the Cosmos DB endpoint."
}
az functionapp config appsettings set --name $functionApp --resource-group $resourceGroup `
  --settings "CosmosDb__Endpoint=$endpoint" "CosmosDb__Database=$database" --output none
```

Changing app settings briefly restarts the Function App. No Cosmos account key or connection
string is required: `DefaultAzureCredential` uses the managed identity in Azure. For local
development, add these same two settings under `Values` in `local.settings.json`.

### 4. Import initial data and verify

The application does **not** seed Cosmos DB automatically. A fresh database initially returns
an empty trail list, and `/api/auth/me` returns `401` until the mock profile exists.
Import the repository's `Data/SeedData.cs` and `Data/MockTrails.cs` records separately, preserving
their string IDs and adding `entity_type` to each document. The mock profile must have
`id: "user-admin"`, `entity_type: "User"` and `user_id: "user-admin"` in `user_data`.
The repository currently has no automated Cosmos seed-import command. Avoid re-importing
with upsert unless overwriting edited records is intentional.

For a read-only smoke test, get the Function App hostname and send its valid server-side
function/host key through a locally supplied environment variable. Do not paste the key
into source code, browser code, or this README.

```powershell
$hostname = az functionapp show --name $functionApp --resource-group $resourceGroup `
  --query defaultHostName --output tsv
if ($LASTEXITCODE -ne 0 -or !$hostname) {
  throw "Could not resolve the Function App hostname."
}
if (!$env:TRAILS_FUNCTION_KEY) { throw "Set TRAILS_FUNCTION_KEY securely before testing." }
$headers = @{ "x-functions-key" = $env:TRAILS_FUNCTION_KEY }
Invoke-RestMethod "https://$hostname/api/trails?limit=1" -Headers $headers
Invoke-RestMethod "https://$hostname/api/auth/me" -Headers $headers
```

Use a host key valid for both endpoints, or their respective function keys for separate tests.
For the frontend's server-side proxy, configure `TRAILS_API_URL` with the Function App's HTTPS
origin and `DEFAULT_API_KEY` with a key valid for the proxied endpoints. Keep Static Web Apps
Microsoft sign-in enabled; its authentication is separate from Cosmos managed-identity access.

### Troubleshooting

| Symptom | Check |
|---------|-------|
| API returns `500` after a Cosmos-backed deployment | Both `CosmosDb__Endpoint` and `CosmosDb__Database` must be set on the **deployed Function App**, not only locally. |
| Cosmos returns `403` | Verify the Function App identity and its database-scoped **data-plane** role; also check network restrictions and role propagation. |
| Role is absent from the identity's portal IAM view | List assignments with `az cosmosdb sql role assignment list`; Cosmos data-plane roles are separate from Azure IAM. |
| `/api/auth/me` returns `401` with a valid function key | Check that the mock `user-admin` document was imported into its matching user partition. |
| Static site loads but its API fails | Check the backend directly with the proxy key, and verify `TRAILS_API_URL` and `DEFAULT_API_KEY`. Unauthenticated `/api/*` requests are expected to return `401`. |

The DOC and DCC alert functions still return mocked empty responses.
