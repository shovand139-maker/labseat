# AGENTS.md · LabSeat

Conventions for anyone changing this repository. Each rule prevents a mistake that is easy to make and easy to miss in review.

## Data access
- All reads and writes go through stored procedures in `db/003_procedures.sql`. No SQL text built from input; no inline SQL in endpoints.
- Every procedure takes `@TenantId` and filters by it. Row-level security is the backstop, not the primary check.
- Open connections only through `SqlConnectionFactory.OpenForTenantAsync`. `OpenMaintenanceAsync` is for migrations and the dev token endpoint only.
- Any procedure that changes seats takes `UPDLOCK, HOLDLOCK` on the event row first, before touching `dbo.Bookings`. Keep that lock order everywhere.
- Business errors are `THROW 500xx` with a user-readable message, mapped in `Errors.cs`. Add the mapping and a unit test when you add a number.

## Migrations
- Add a new numbered script; never edit one that has shipped. Scripts are embedded resources and run in name order, split on `GO` lines.
- Use `CREATE OR ALTER` for procedures so a script can be re-applied during development.

## API
- Endpoints read the caller with `CurrentUser.From(http.User)`. Never accept a tenant or user id from the body, query or route.
- Validation failures return 422 problem details with field errors keyed by camelCase property name.
- Something the caller may not see returns 404, not 403, so ids do not leak across tenants or learners.

## Front end
- HTTP goes through `ApiService`. The auth interceptor only adds tokens to `/api/` URLs.
- Use signals for view state and RxJS for streams (polling, search, refresh); `switchMap` for anything that can be superseded.

## Definition of done
1. New behaviour has an integration test that fails without the change.
2. `dotnet test` (with the SQL Server container), `npm run test:ci` and `npx ng build` pass.
3. Concurrency-sensitive changes extend `ConcurrencyTests`.
4. The PR description says what changed, why, how it was tested, and how to roll back.
