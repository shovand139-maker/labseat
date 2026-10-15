using Microsoft.AspNetCore.Mvc;

namespace LabSeat.Api;

public static class Endpoints
{
    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] }, statusCode: StatusCodes.Status422UnprocessableEntity);

    public static WebApplication MapLabSeatEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization(Policies.Member);

        api.MapGet("/events", async (HttpContext http, LabSeatRepository repo, TimeProvider clock,
            DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
        {
            var start = from ?? clock.GetUtcNow().AddHours(-12);
            var end = to ?? start.AddDays(60);
            if (end <= start)
            {
                return Invalid("to", "'to' must be after 'from'.");
            }
            return Results.Ok(await repo.ListEventsAsync(CurrentUser.From(http.User), start, end, ct));
        });

        api.MapGet("/events/{eventId:guid}", async (HttpContext http, LabSeatRepository repo, Guid eventId, CancellationToken ct) =>
            await repo.GetEventAsync(CurrentUser.From(http.User), eventId, ct) is { } row
                ? Results.Ok(row)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Event not found"));

        api.MapPost("/events", async (HttpContext http, LabSeatRepository repo, TimeProvider clock,
            CreateEventRequest request, CancellationToken ct) =>
        {
            var errors = request.Validate(clock.GetUtcNow());
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            var created = await repo.CreateEventAsync(CurrentUser.From(http.User), request, ct);
            return Results.Created($"/api/events/{created.EventId}", created);
        }).RequireAuthorization(Policies.Instructor);

        api.MapPut("/events/{eventId:guid}/capacity", async (HttpContext http, LabSeatRepository repo,
            Guid eventId, SetCapacityRequest request, CancellationToken ct) =>
            request.Capacity is < 1 or > 500
                ? Invalid("capacity", "Capacity must be between 1 and 500.")
                : Results.Ok(await repo.SetCapacityAsync(CurrentUser.From(http.User), eventId, request.Capacity, ct)))
            .RequireAuthorization(Policies.Instructor);

        api.MapGet("/events/{eventId:guid}/roster", async (HttpContext http, LabSeatRepository repo, Guid eventId, CancellationToken ct) =>
            Results.Ok(await repo.GetRosterAsync(CurrentUser.From(http.User), eventId, ct)))
            .RequireAuthorization(Policies.Instructor);

        api.MapPost("/events/{eventId:guid}/bookings", async (HttpContext http, LabSeatRepository repo, Guid eventId,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
            idempotencyKey is { Length: > 100 }
                ? Invalid("Idempotency-Key", "Use at most 100 characters.")
                : Results.Ok(await repo.BookAsync(CurrentUser.From(http.User), eventId, idempotencyKey, ct)))
            .RequireAuthorization(Policies.Learner)
            .RequireRateLimiting("bookings");

        api.MapDelete("/bookings/{bookingId:guid}", async (HttpContext http, LabSeatRepository repo, Guid bookingId, CancellationToken ct) =>
            Results.Ok(await repo.CancelAsync(CurrentUser.From(http.User), bookingId, ct)));

        api.MapGet("/me/bookings", async (HttpContext http, LabSeatRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.ListMyBookingsAsync(CurrentUser.From(http.User), ct)))
            .RequireAuthorization(Policies.Learner);

        // Development and test hosts only: lets the Angular app and the tests sign in as demo users.
        if (app.Configuration.GetValue<bool>("Auth:EnableDevTokens"))
        {
            var dev = app.MapGroup("/dev").AllowAnonymous();

            dev.MapGet("/users", async (LabSeatRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.ListDemoUsersAsync(ct)));

            dev.MapPost("/token", async (DevTokenRequest request, LabSeatRepository repo, IConfiguration config, CancellationToken ct) =>
            {
                var user = await repo.FindUserByEmailAsync(request.Email?.Trim() ?? "", ct);
                if (user is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No user with that email");
                }
                var lifetime = TimeSpan.FromHours(8);
                return Results.Ok(new DevTokenResponse(DevTokens.Create(config, user, lifetime), "Bearer", (int)lifetime.TotalSeconds, user));
            });
        }

        return app;
    }
}
