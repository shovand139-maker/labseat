using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace LabSeat.Api;

/// <summary>Maps the error numbers our stored procedures THROW (and a few engine errors) to HTTP.</summary>
public static class SqlErrors
{
    public sealed record Mapping(int Status, string Title, bool ExposeMessage);

    public static Mapping? Map(int number) => number switch
    {
        50001 => new(StatusCodes.Status404NotFound, "Event not found", true),
        50003 => new(StatusCodes.Status409Conflict, "Booking closed", true),
        50004 => new(StatusCodes.Status409Conflict, "Capacity conflict", true),
        50005 => new(StatusCodes.Status404NotFound, "Booking not found", true),
        50006 => new(StatusCodes.Status409Conflict, "Already cancelled", true),
        50007 => new(StatusCodes.Status403Forbidden, "Not allowed", true),
        50008 => new(StatusCodes.Status422UnprocessableEntity, "Invalid request", true),
        // Unique index violations: never echo engine text (it names indexes and key values).
        2601 or 2627 => new(StatusCodes.Status409Conflict, "Conflict", false),
        // Row-level security block predicate.
        33504 => new(StatusCodes.Status403Forbidden, "Not allowed", false),
        _ => null,
    };
}

public sealed class SqlExceptionHandler(IProblemDetailsService problems, ILogger<SqlExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not SqlException sql || SqlErrors.Map(sql.Number) is not { } mapping)
        {
            return false;
        }

        if (!mapping.ExposeMessage)
        {
            logger.LogWarning(sql, "SQL error {Number} mapped to {Status}", sql.Number, mapping.Status);
        }

        context.Response.StatusCode = mapping.Status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = mapping.Status,
                Title = mapping.Title,
                Detail = mapping.ExposeMessage ? sql.Message : "The request conflicts with the current state.",
                Extensions = { ["code"] = sql.Number },
            },
        });
    }
}
