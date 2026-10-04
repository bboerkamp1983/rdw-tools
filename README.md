# rdw-tools
Reusable .NET library, CLI and REST API for RDW open data

Data source: RDW open data, dataset `m9d7-ebf2` ("Gekentekende voertuigen").
Design decisions are in [`docs/adr/`](docs/adr/).

## Requirements

.NET 10 SDK.

## Build and test

```sh
dotnet build
dotnet test
```

## CLI

```sh
dotnet run --project src/Rdw.Cli -- kenteken X998ZG
```

Ctrl+C cancels a running lookup (exit code 130).

## REST API

```sh
dotnet run --project src/Rdw.Api --launch-profile http
```

Every endpoint except `/health` requires a Google ID token from an allowed
account, sent as `Authorization: Bearer <token>` (ADR-005).

| Situation | HTTP status |
| --- | --- |
| Vehicle found | 200, vehicle as JSON |
| Plate not in the RDW data | 404, problem details |
| Input can never be a plate | 400, problem details |
| RDW unreachable | 503, problem details |
| No token, or an invalid or expired token | 401 |
| Valid token, but the account is not allowed | 403 |

`GET http://localhost:5064/health` returns 200 (`Healthy`) while the API is running.
It needs no token and does not call the RDW.

The OpenAPI document is at `http://localhost:5064/openapi/v1.json` (Development only,
token required).
Fields and examples: [ADR-004](docs/adr/004-rest-api.md).

### Configuration and local testing

Access is configured with three settings. Locally, store them with
`dotnet user-secrets` (kept in your user profile, never in the repository).
In production they come from environment variables or the platform's secret store.
Never put them in `appsettings*.json`: the allow-list is personal data.

```sh
# The Google OAuth client ID of this API (the expected "aud" of the token). Not a secret.
dotnet user-secrets set "Authentication:Google:ClientId" "<client-id>.apps.googleusercontent.com" --project src/Rdw.Api

# Optional: allow a whole Google Workspace domain (checked on the "hd" claim, never on the email address).
dotnet user-secrets set "Authorization:AllowedHostedDomains:0" "example.com" --project src/Rdw.Api

# Optional: allow individual Google accounts by their "sub" claim (not by email).
dotnet user-secrets set "Authorization:AllowedSubjects:0" "<sub>" --project src/Rdw.Api

dotnet user-secrets list --project src/Rdw.Api
```

Both allow-lists are empty by default, which denies everyone. Without a client
ID, every token is rejected with 401.

Check the lock by hand:

```sh
curl -i http://localhost:5064/health                     # 200, no token needed
curl -i http://localhost:5064/api/v1/vehicles/X998ZG     # 401 Unauthorized
curl -i -H "Authorization: Bearer <id-token>" http://localhost:5064/api/v1/vehicles/X998ZG
                                                         # 200 if allowed, 403 if not
```

How a script obtains a Google ID token for this client ID is not settled yet
(ADR-005, "What could not be verified"). Never paste a real token into an online
decoder or a chat: it gives access to the API until it expires. The automated
tests (`dotnet test`) cover all access rules with locally signed test tokens and
never contact Google.
