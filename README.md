# LabSeat

[![CI](https://github.com/shovand139-maker/labseat/actions/workflows/ci.yml/badge.svg)](https://github.com/shovand139-maker/labseat/actions/workflows/ci.yml)

Seat booking for instructor-led cloud lab events. Instructors schedule events with a fixed number of seats; learners book, join a waitlist when it is full, and move up automatically when someone cancels. Built as a multi-tenant service: every organization sees only its own events, people and bookings.

**Stack:** ASP.NET Core 8 minimal API · SQL Server stored procedures (Dapper) · SQL Server row-level security · Angular 18 (standalone components, signals, RxJS) · xUnit integration tests against a real SQL Server · GitHub Actions · Bicep for Azure.

## The interesting parts

**Seat allocation lives in the database.** `usp_Booking_Create` takes an `UPDLOCK, HOLDLOCK` on the event row, counts confirmed seats and either confirms or appends to the waitlist inside one transaction. Cancelling and changing capacity take the same lock first, so the three can never deadlock or overbook. A filtered unique index (one active booking per learner per event) backs the rule up. `ConcurrencyTests` fires 40 simultaneous bookings at a 10-seat event and asserts exactly 10 confirmations and a gap-free waitlist of 30.

**Tenant isolation has two layers.** Every procedure filters by `@TenantId`. Underneath, a SQL Server security policy (`db/002_row_level_security.sql`) filters and blocks rows by `SESSION_CONTEXT('TenantId')`, which the API sets read-only on each connection it opens. If a procedure ever forgets its filter, other tenants' rows are still invisible, and inserting a row for another tenant fails with error 33504. Both behaviours have tests.

**Retries are safe.** Booking accepts an `Idempotency-Key` header; a repeated request (or a double click without a key) returns the original booking instead of creating a second one or failing.

**Errors are problem details.** Procedures `THROW` numbered errors (50001–50008). One exception handler maps them to `application/problem+json` with the right status (404, 409, 403, 422). Engine messages for duplicate-key and RLS errors are never echoed to clients.

## API

| Method | Route | Who |
|---|---|---|
| GET | `/api/events?from&to` | anyone in the organization |
| GET | `/api/events/{id}` | anyone in the organization |
| POST | `/api/events` | instructors |
| PUT | `/api/events/{id}/capacity` | instructors (extra seats go to the waitlist in order) |
| GET | `/api/events/{id}/roster` | instructors |
| POST | `/api/events/{id}/bookings` | learners, `Idempotency-Key` header, rate limited per learner |
| DELETE | `/api/bookings/{id}` | the learner who booked, or an instructor |
| GET | `/api/me/bookings` | learners |
| GET | `/healthz` | health check including the database |

Authentication is JWT bearer. In production set `Auth:Authority` and `Auth:Audience` to a Microsoft Entra ID app registration; in development the API issues short-lived HS256 tokens for the seeded demo users at `POST /dev/token`.

## Tests

| Project | What it covers |
|---|---|
| `tests/LabSeat.Api.Tests` (unit) | SQL error mapping, request validation, migration script ordering and batch splitting, dev token claims |
| `tests/LabSeat.Api.Tests` (integration) | Booking, waitlist and promotion flows; idempotent retries; capacity changes; ownership; 401/403/404/422; RLS read and write isolation; 40-way concurrency |
| `web` (Karma/Jasmine) | API client headers and URLs, auth and problem interceptors, form validators, debounced event filtering |

CI runs the integration tests against SQL Server 2022 in a service container, builds and tests the Angular app in headless Chrome, and compiles the Bicep template.

## Run it locally

```bash
docker compose up -d sqlserver                  # SQL Server 2022 on localhost:1433
dotnet run --project src/LabSeat.Api            # http://localhost:5080, creates and migrates the database
cd web && npm ci && npx ng serve                # http://localhost:4200, proxies /api to the API
dotnet test                                      # needs the SQL Server container
```

Sign in as `rahul@contoso.example` (instructor) or `ananya@contoso.example` (learner). Fabrikam University users cannot see Contoso data.

## Project layout

```
db/                      numbered migration scripts: schema, row-level security, procedures, demo seed
src/LabSeat.Api/         minimal API: endpoints, auth, data access, error mapping, migration runner
tests/LabSeat.Api.Tests/ xUnit unit and integration tests
web/                     Angular 18 front end
infra/main.bicep         App Service, Azure SQL (Entra-only auth), Static Web Apps, Application Insights
AGENTS.md                conventions and definition of done for contributors
```

## Deploying to Azure

`infra/main.bicep` creates the API with a system-assigned managed identity and connects to Azure SQL with `Authentication=Active Directory Managed Identity`, so there is no database password. After the first deployment, an Entra admin creates the database user for the API identity (`CREATE USER [<api name>] FROM EXTERNAL PROVIDER`) and grants `EXECUTE` on `dbo`; migrations run from the pipeline with the admin identity.

## Author

Shovan Dutta · [github.com/shovand139-maker](https://github.com/shovand139-maker)
