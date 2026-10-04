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

Then open `http://localhost:5064/api/v1/vehicles/X998ZG`.

| Situation | HTTP status |
| --- | --- |
| Vehicle found | 200, vehicle as JSON |
| Plate not in the RDW data | 404, problem details |
| Input can never be a plate | 400, problem details |
| RDW unreachable | 503, problem details |

The OpenAPI document is at `http://localhost:5064/openapi/v1.json` (Development only).
Fields and examples: [ADR-004](docs/adr/004-rest-api.md).
