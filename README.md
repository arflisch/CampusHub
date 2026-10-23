# CampusHub

[![CI](https://github.com/arflisch/CampusHub/actions/workflows/ci.yml/badge.svg)](https://github.com/arflisch/CampusHub/actions/workflows/ci.yml)
[![CodeQL](https://github.com/arflisch/CampusHub/actions/workflows/codeql.yml/badge.svg)](https://github.com/arflisch/CampusHub/actions/workflows/codeql.yml)

A course enrollment platform built as a .NET microservices sandbox: teachers publish courses with a limited number of
seats, students enroll, and each enrollment runs as a saga across services.

## Architecture

| Part | Role |
|---|---|
| `BE/Gtw` | YARP gateway and BFF: OpenID Connect sign-in, HttpOnly cookie session, tokens attached server side, CSRF check, serves the Blazor front |
| `BE/Catalog` | Courses and seat reservations (MongoDB, optimistic concurrency) |
| `BE/Enrollment` | Enrollment saga on Dapr Workflow (hold a seat, confirm it, compensate on failure), transactional outbox on MongoDB, Hangfire jobs |
| `BE/Notification` | Idempotent subscriber of the enrollment events, "my notifications" |
| `BE/Grading`, `BE/Billing` | Scaffolded services |
| `FE/CampusHub.Web` | Blazor WebAssembly front, MVVM (CommunityToolkit.Mvvm), thin views over plain C# ViewModels |
| `Aspire` | Local orchestration: MongoDB replica set, RabbitMQ, Redis, Dapr sidecars, OpenTelemetry dashboard |
| `Dapr` | Components per environment: RabbitMQ pub/sub, Redis state store, resiliency policies, subscriptions |

Each service follows the same layers: `Domain` (aggregates returning `FluentResults` results, no exceptions for expected
refusals), `UseCases`, `FrontendApi` / `ServiceApi` (minimal APIs, ProblemDetails), `Infrastructure` and `Host`.

**Enrollment flow**: the front sends a request with an idempotency key → Enrollment stores it and starts the workflow →
the workflow reserves a seat in Catalog, then confirms it (or releases it) → the confirmation is written with an outbox
message in the same MongoDB transaction → the dispatcher publishes it through Dapr to RabbitMQ → Notification stores a
notification, ignoring duplicates.

## Stack

.NET 11 · ASP.NET Core minimal APIs · YARP · Dapr (Workflow, pub/sub, state, resiliency) · RabbitMQ · MongoDB · Redis ·
Hangfire · .NET Aspire · OpenTelemetry + Serilog · Blazor WebAssembly (MVVM) · FluentResults · xUnit, Testcontainers ·
[Arc4u](https://github.com/Arc4u-org/Arc4u) framework

## Run it

Requirements: .NET 11 SDK (see `global.json`), Docker, Dapr CLI with `dapr init` (runtime 1.18 or later).

**Front only, with an in-memory backend:**

```bash
dotnet run --project FE/CampusHub.Web -p:WasmApplicationEnvironmentName=Demo
```

Then open http://localhost:5280.

**Everything** (containers, services, sidecars, gateway):

```bash
dotnet run --project Aspire
```

The gateway listens on https://localhost:7184. The services need an OpenID Connect client and the Arc4u decryption
certificate; their secrets are read from `Configs/appsettings.Development.local.json` next to each service's
`appsettings.Development.json`, a file that is not committed.

**Roles:** every signed-in user is a student. Teachers (create and publish courses) and administrators (Swagger,
Hangfire dashboards) come from the identity provider (`campushub:teach` / `campushub:admin` permissions) or, locally, from
the AppHost user secrets:

```bash
dotnet user-secrets set CampusHub:Teachers "<user id>" --project Aspire
```

The user id is shown on the Teaching page to a signed-in user who is not a teacher yet.

**Tests:**

```bash
dotnet test CampusHub.slnx
```

The outbox tests start a MongoDB replica set with Testcontainers, so Docker must be running.
