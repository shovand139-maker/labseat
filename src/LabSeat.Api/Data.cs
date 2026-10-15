using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LabSeat.Api;

/// <summary>
/// Opens SQL connections that are pinned to one tenant. SESSION_CONTEXT is set read-only, so code
/// running on the connection cannot switch tenants, and the row-level security policy uses it.
/// </summary>
public sealed class SqlConnectionFactory(IConfiguration config)
{
    private readonly string _connectionString = config.GetConnectionString("LabSeat")
        ?? throw new InvalidOperationException("ConnectionStrings:LabSeat is not configured.");

    public string ConnectionString => _connectionString;

    public Task<SqlConnection> OpenForTenantAsync(Guid tenantId, CancellationToken ct) =>
        OpenWithContextAsync("TenantId", tenantId, SqlDbType.UniqueIdentifier, ct);

    /// <summary>Bypasses tenant isolation. Used only by migrations and the development token endpoint.</summary>
    public Task<SqlConnection> OpenMaintenanceAsync(CancellationToken ct) =>
        OpenWithContextAsync("Maintenance", true, SqlDbType.Bit, ct);

    private async Task<SqlConnection> OpenWithContextAsync(string key, object value, SqlDbType type, CancellationToken ct)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_set_session_context @key = @key, @value = @value, @read_only = 1;";
        command.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 128) { Value = key });
        command.Parameters.Add(new SqlParameter("@value", type) { Value = value });
        await command.ExecuteNonQueryAsync(ct);
        return connection;
    }
}

public sealed class EventRow
{
    public Guid EventId { get; set; }
    public string Title { get; set; } = "";
    public string LabTemplate { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public int Capacity { get; set; }
    public int Confirmed { get; set; }
    public int Waitlisted { get; set; }
    public int SeatsLeft => Math.Max(0, Capacity - Confirmed);
    public Guid? MyBookingId { get; set; }
    public string? MyStatus { get; set; }
}

public sealed class BookingRow
{
    public Guid BookingId { get; set; }
    public Guid EventId { get; set; }
    public string EventTitle { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public Guid LearnerId { get; set; }
    public string Status { get; set; } = "";
    public int? QueuePosition { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RosterRow
{
    public Guid BookingId { get; set; }
    public Guid LearnerId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Status { get; set; } = "";
    public int? QueuePosition { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CancelResult
{
    public Guid CancelledBookingId { get; set; }
    public Guid? PromotedBookingId { get; set; }
}

public sealed class UserRow
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public string TenantName { get; set; } = "";
}

/// <summary>Thin wrapper over the stored procedures. No SQL text is built from input.</summary>
public sealed class LabSeatRepository(SqlConnectionFactory db)
{
    private static CommandDefinition Proc(string name, object args, CancellationToken ct) =>
        new(name, args, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    public async Task<IReadOnlyList<EventRow>> ListEventsAsync(CurrentUser user, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var conn = await db.OpenForTenantAsync(user.TenantId, ct);
        var rows = await conn.QueryAsync<EventRow>(Proc("dbo.usp_Event_List", new
        {
            user.TenantId,
            From = from.UtcDateTime,
            To = to.UtcDateTime,
            LearnerId = user.IsInstructor ? (Guid?)null : user.UserId,
        }, ct));
        return rows.AsList();
    }

    public async Task<EventRow?> GetEventAsync(CurrentUser user, Guid eventId, CancellationToken ct)
    {
        await using var conn = await db.OpenForTenantAsync(user.TenantId, ct);
        return await conn.QuerySingleOrDefaultAsync<EventRow>(Proc("dbo.usp_Event_Get", new
        {
            user.TenantId,
            EventId = eventId,
            LearnerId = user.IsInstructor ? (Guid?)null : user.UserId,
        }, ct));
    }

    public async Task<EventRow> CreateEventAsync(CurrentUser user, CreateEventRequest request, CancellationToken ct)
    {
        await using var conn = await db.OpenForTenantAsync(user.TenantId, ct);
        return await conn.QuerySingleAsync<EventRow>(Proc("dbo.usp_Event_Create", new
        {
            user.TenantId,
            EventId = Guid.NewGuid(),
            Title = request.Title.Trim(),
            LabTemplate = request.LabTemplate.Trim(),
            StartsAt = request.StartsAt.UtcDateTime,
            EndsAt = request.EndsAt.UtcDateTime,
            request.Capacity,
            CreatedBy = user.UserId,
        }, ct));
    }

    public async Task<EventRow> SetCapacityAsync(CurrentUser user, Guid eventId, int capacity, CancellationToken ct)
    {
        await using var conn = await db.OpenForTenantAsync(user.TenantId, ct);
        return await conn.QuerySingleAsync<EventRow>(Proc("dbo.usp_Event_SetCapacity", new
        {
            user.TenantId,
            EventId = eventId,
            Capacity = capacity,
        }, ct));
    }

    public async Task<IReadOnlyList<RosterRow>> GetRosterAsync(CurrentUser user, Guid eventId, CancellationToken ct)
    {
        await using var conn = await db.OpenForTenantAsync(user.TenantId, ct);
        var rows = await conn.QueryAsync<RosterRow>(Proc("dbo.usp_Event_Roster", new { user.TenantId, EventId = eventId }, ct));
        return rows.AsList();
    }

    public async Task<BookingRow> BookAsync(CurrentUser user, Guid eventId, string? idempotencyKey, CancellationToken ct)
    {
        await using var conn = await db.OpenForTenantAsync(user.TenantId, ct);
        return await conn.QuerySingleAsync<BookingRow>(Proc("dbo.usp_Booking_Create", new
        {
            user.TenantId,
            EventId = eventId,
            LearnerId = user.UserId,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
        }, ct));
    }

    public async Task<CancelResult> CancelAsync(CurrentUser user, Guid bookingId, CancellationToken ct)
    {
        await using var conn = await db.OpenForTenantAsync(user.TenantId, ct);
        return await conn.QuerySingleAsync<CancelResult>(Proc("dbo.usp_Booking_Cancel", new
        {
            user.TenantId,
            BookingId = bookingId,
            ActorId = user.UserId,
            ActorIsInstructor = user.IsInstructor,
        }, ct));
    }

    public async Task<IReadOnlyList<BookingRow>> ListMyBookingsAsync(CurrentUser user, CancellationToken ct)
    {
        await using var conn = await db.OpenForTenantAsync(user.TenantId, ct);
        var rows = await conn.QueryAsync<BookingRow>(Proc("dbo.usp_Booking_ListForLearner", new { user.TenantId, LearnerId = user.UserId }, ct));
        return rows.AsList();
    }

    // Development only: user lookup across tenants for the sign-in picker.
    public async Task<IReadOnlyList<UserRow>> ListDemoUsersAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenMaintenanceAsync(ct);
        var rows = await conn.QueryAsync<UserRow>(new CommandDefinition(
            """
            SELECT u.UserId, u.TenantId, u.Email, u.DisplayName, u.Role, t.Name AS TenantName
            FROM dbo.Users AS u JOIN dbo.Tenants AS t ON t.TenantId = u.TenantId
            ORDER BY t.Name, u.Role DESC, u.DisplayName
            """, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<UserRow?> FindUserByEmailAsync(string email, CancellationToken ct)
    {
        await using var conn = await db.OpenMaintenanceAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            """
            SELECT u.UserId, u.TenantId, u.Email, u.DisplayName, u.Role, t.Name AS TenantName
            FROM dbo.Users AS u JOIN dbo.Tenants AS t ON t.TenantId = u.TenantId
            WHERE u.Email = @Email
            """, new { Email = email }, cancellationToken: ct));
    }
}

public sealed class DatabaseHealthCheck(SqlConnectionFactory db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(db.ConnectionString);
            await conn.OpenAsync(ct);
            await conn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: ct));
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable", ex);
        }
    }
}
