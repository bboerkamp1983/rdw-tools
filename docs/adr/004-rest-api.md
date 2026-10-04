# ADR-004: REST API

- Status: Proposed
- Date: 2026-10-04

## Context

The solution has a core library (`Rdw.Core`) and a CLI. The REST API is the
next planned front-end (ADR-001). Like the CLI, it must contain no RDW logic:
it calls `IRdwClient` and translates the `VehicleLookupResult` into HTTP
(ADR-002). Later front-ends (a web UI) and features (caching, Docker, more
datasets) will build on this API, so its public contract should be stable
and independent of internal Core types.

## Decision

### Project and framework

- New project `src/Rdw.Api`: ASP.NET Core on .NET 10 with **minimal APIs**
  (no MVC controllers). One endpoint does not need controller structure, and
  minimal APIs are the current default for small HTTP services.
- New test project `tests/Rdw.Api.Tests` (xUnit). Both are added to
  `RdwTools.slnx`, so the existing CI (`dotnet build`, `dotnet test`) picks
  them up without changes to `.github/`.

### Endpoint

`GET /api/v1/vehicles/{licensePlate}`

- The plate is passed as typed by the user (`x-998-zg`, `X998ZG`);
  normalization happens in Core, as in the CLI.
- English, plural resource names; the `v1` prefix lets us change the
  contract later without breaking existing callers.

### Status mapping

| `LookupStatus` | HTTP status | Body |
| --- | --- | --- |
| `Found` | 200 OK | vehicle (see below) |
| `NotFound` | 404 Not Found | problem details |
| `InvalidInput` | 400 Bad Request | problem details |
| `ServiceUnavailable` | 503 Service Unavailable | problem details |

`NotFound` stays a 404 and never becomes "invalid": the RDW data is the
source of truth (ADR-002).

### Response shape

- Success: a `VehicleResponse` defined in `Rdw.Api`, serialized as camelCase
  JSON. It copies the fields of `Core.Vehicle` (plate, make, trade name,
  vehicle type, color, empty mass, first admission date, APK expiry date,
  `isExported`). Dates use ISO 8601 (`2028-03-20`); unknown values are
  `null`, never left out. A separate type keeps renaming inside Core from
  silently changing the public API. Mapping is a plain copy, not RDW logic.
- Errors: RFC 9457 Problem Details (`application/problem+json`), built into
  ASP.NET Core. `detail` carries the `Message` from the lookup result; the
  normalized plate is included for 404.

### HttpClient and configuration

- `RdwClient` is registered with `IHttpClientFactory`
  (`AddHttpClient<IRdwClient, RdwClient>`), with a 15-second timeout like the
  CLI. This avoids socket exhaustion and is the standard .NET pattern.
- Settings come from `appsettings.json` and environment variables. No
  secrets are needed now; if an RDW app token is added later, it comes from
  configuration or environment, never from the repository.

### Documentation of the API

- The built-in OpenAPI document (`Microsoft.AspNetCore.OpenApi`) is served at
  `/openapi/v1.json` in Development. A browsable UI is out of scope for now.

### Testing

- Endpoint tests use `WebApplicationFactory` and replace `IRdwClient` with a
  fake that returns each `LookupStatus`. API tests never call the RDW.
- One test per row of the status mapping, plus the JSON shape of a found
  vehicle.

## Out of scope (separate issues or ADRs later)

Authentication, rate limiting, caching, CORS, Docker, a health endpoint,
HTTPS certificates for production, and a web UI.

## Consequences

- One more project to build and test; CI time grows slightly.
- `VehicleResponse` must be kept in step with `Core.Vehicle` when fields are
  added; a test on the JSON shape catches forgotten fields in the response.
- `IRdwClient.GetVehicleAsync` has no `CancellationToken`. The API works
  without it, but a cancelled HTTP request would still wait for the RDW.
  Adding an optional token to Core is a small follow-up change.
- With 503 for every RDW failure, callers cannot tell a timeout from an RDW
  error by status code alone; the problem `detail` explains it.
