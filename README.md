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
| Rate limit reached | 429, problem details, `Retry-After` header |

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

### Rate limits

Requests to the API are limited per user (the Google `sub` claim, never the
email address or IP address) and for all users together (ADR-005). A rejected
request gets `429 Too Many Requests` with a `Retry-After` header (seconds), so it
is never confused with `503` (RDW unavailable). Requests answered with 401 or 403
use no quota, and `/health` is not rate limited. Every other request to the
vehicle endpoint counts, including 400 and 404 answers (this also limits scraping).
`Retry-After` is an upper bound: the full window length, not the exact time left.
While the global limit is reached, retries still count against the user's own
limit, so a user may have to wait up to one extra per-user window.

| Setting | Default | Meaning |
| --- | --- | --- |
| `RateLimiting:PerUser:PermitLimit` | 10 | Requests per user per window |
| `RateLimiting:PerUser:WindowSeconds` | 60 | Length of the per-user window |
| `RateLimiting:Global:PermitLimit` | 60 | Requests for all users together per window |
| `RateLimiting:Global:WindowSeconds` | 60 | Length of the global window |

The defaults are **conservative guesses, to be verified**. They are not based on
RDW limits: no official RDW limits were found (ADR-005, "What could not be
verified"). Every value must be greater than zero; otherwise the API does not start.
Set them like the other settings, e.g.
`dotnet user-secrets set "RateLimiting:Global:PermitLimit" "30" --project src/Rdw.Api`.

The limits are **per app instance**: each running copy of the API keeps its own
counters in memory. The official documentation does not say this explicitly; the
test `TwoAppInstances_DoNotShareLimits` shows it. With several replicas the global
limit multiplies by the number of replicas, so the hosting setup must either run
one replica or accept that.
