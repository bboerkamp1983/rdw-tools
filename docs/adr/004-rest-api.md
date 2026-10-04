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

### Vehicle data

The API returns the vehicle data that `Rdw.Core` maps, no more and no less
(`bodyType` and `maxPermittedMassKg` are added to Core in PR #40). Every field is always present; an unknown value is `null`, never
left out.

| JSON field | Type | Meaning | RDW source field |
| --- | --- | --- | --- |
| `licensePlate` | string | Plate as stored by the RDW, normalized (`X998ZG`) | `kenteken` |
| `make` | string or null | Make (`KIA`) | `merk` |
| `tradeName` | string or null | Trade name / model (`NIRO`) | `handelsbenaming` |
| `vehicleType` | string or null | Vehicle type (`Personenauto`) | `voertuigsoort` |
| `primaryColor` | string or null | Primary color (`GRIJS`) | `eerste_kleur` |
| `bodyType` | string or null | Body type (`stationwagen`) | `inrichting` |
| `emptyMassKg` | integer or null | Empty mass in kg (`1657`) | `massa_ledig_voertuig` |
| `maxPermittedMassKg` | integer or null | Maximum permitted mass in kg (`2200`) | `toegestane_maximum_massa_voertuig` |
| `firstAdmissionDate` | date or null | Date of first admission (`2024-03-20`) | `datum_eerste_toelating` |
| `apkExpiryDate` | date or null | APK (MOT) expiry date (`2028-03-20`) | `vervaldatum_apk` |
| `isExported` | boolean or null | Vehicle has been exported (`Ja`/`Nee`) | `export_indicator` |

- Texts are passed on as the RDW provides them (Dutch, upper case for
  colors and makes); the API does not translate them. This includes RDW
  placeholders such as `Niet geregistreerd` or `N.v.t.` in `bodyType`.
- Dates are ISO 8601 (`yyyy-MM-dd`), without time.
- Adding a field is done in Core first (`RdwVehicleRecord`, `Vehicle`, with
  tests), then in the API response. Adding a field is a compatible change
  within `v1`; renaming or removing one needs `v2`.
- Data from other RDW datasets (fuel, APK history, recalls) is out of scope
  for this ADR.

### Response shape

- Success: a `VehicleResponse` defined in `Rdw.Api`, serialized as camelCase
  JSON with exactly the fields above. A separate type keeps renaming inside
  Core from silently changing the public API. Mapping is a plain copy, not
  RDW logic.
- Errors: RFC 9457 Problem Details (`application/problem+json`), built into
  ASP.NET Core. For 400 and 503, `detail` carries the `Message` from the
  lookup result. A `NotFound` result has no message, so for 404 the API uses
  a fixed text (as the CLI does) and adds the normalized plate as
  `licensePlate`.

Example: `GET /api/v1/vehicles/x-998-zg` returns 200 OK:

```json
{
  "licensePlate": "X998ZG",
  "make": "KIA",
  "tradeName": "NIRO",
  "vehicleType": "Personenauto",
  "primaryColor": "GRIJS",
  "bodyType": "stationwagen",
  "emptyMassKg": 1657,
  "maxPermittedMassKg": 2200,
  "firstAdmissionDate": "2024-03-20",
  "apkExpiryDate": "2028-03-20",
  "isExported": false
}
```

Example: `GET /api/v1/vehicles/ZZ-999-Z` returns 404 Not Found:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Vehicle not found",
  "status": 404,
  "detail": "No vehicle found for ZZ999Z in the RDW open data.",
  "licensePlate": "ZZ999Z"
}
```

Example: `GET /api/v1/vehicles/AB12` returns 400 Bad Request:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Invalid license plate",
  "status": 400,
  "detail": "The input can never be a license plate (a plate has exactly six letters or digits, separators not counted)."
}
```

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
