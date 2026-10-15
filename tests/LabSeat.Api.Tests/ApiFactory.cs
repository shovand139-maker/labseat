using System.Data;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace LabSeat.Api.Tests;

/// <summary>
/// Hosts the real API against a real SQL Server (CI starts one as a service container).
/// Startup creates the database and applies the migrations, including the demo seed.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "test-signing-key-for-labseat-integration-tests-0123456789";

    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("LABSEAT_TEST_SQL")
        ?? "Server=localhost,1433;Database=LabSeatTests;User Id=sa;Password=LabSeat!Passw0rd;TrustServerCertificate=True";

    public static readonly Guid Contoso = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Fabrikam = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Lena = Guid.Parse("b0000000-0000-0000-0000-000000000001");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:LabSeat", ConnectionString);
        builder.UseSetting("Database:ApplyMigrations", "true");
        builder.UseSetting("Database:CreateIfMissing", "true");
        builder.UseSetting("Auth:EnableDevTokens", "true");
        builder.UseSetting("Auth:DevSigningKey", SigningKey);
    }

    public Task InitializeAsync()
    {
        _ = Server; // starts the host: database, migrations, seed
        return Task.CompletedTask;
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    public async Task<HttpClient> ClientForAsync(string email)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/dev/token", new { email });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    public static Task<SqlConnection> OpenMaintenanceAsync() => OpenAsync("Maintenance", true, SqlDbType.Bit);

    public static Task<SqlConnection> OpenTenantAsync(Guid tenantId) => OpenAsync("TenantId", tenantId, SqlDbType.UniqueIdentifier);

    private static async Task<SqlConnection> OpenAsync(string key, object value, SqlDbType type)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_set_session_context @key = @key, @value = @value, @read_only = 1;";
        command.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 128) { Value = key });
        command.Parameters.Add(new SqlParameter("@value", type) { Value = value });
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private sealed record TokenResponse(string AccessToken);
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
}

public sealed record EventDto(Guid EventId, string Title, int Capacity, int Confirmed, int Waitlisted, int SeatsLeft, Guid? MyBookingId, string? MyStatus);

public sealed record BookingDto(Guid BookingId, Guid EventId, string Status, int? QueuePosition);

public sealed record CancelDto(Guid CancelledBookingId, Guid? PromotedBookingId);

public sealed record RosterDto(Guid BookingId, string DisplayName, string Status, int? QueuePosition);

public sealed record ProblemDto(int Status, string Title, int? Code, Dictionary<string, string[]>? Errors);

public static class Api
{
    public const string Instructor = "rahul@contoso.example";
    public const string Ananya = "ananya@contoso.example";
    public const string Kabir = "kabir@contoso.example";
    public const string Zoya = "zoya@contoso.example";
    public const string FabrikamInstructor = "lena@fabrikam.example";
    public const string Arjun = "arjun@fabrikam.example";

    public static async Task<EventDto> CreateEventAsync(HttpClient instructor, int capacity, DateTimeOffset? startsAt = null)
    {
        var start = startsAt ?? DateTimeOffset.UtcNow.AddDays(3).AddMinutes(Random.Shared.Next(0, 5000));
        var response = await instructor.PostAsJsonAsync("/api/events", new
        {
            title = $"Test event {Guid.NewGuid().ToString("N")[..8]}",
            labTemplate = "aks-fundamentals",
            startsAt = start,
            endsAt = start.AddHours(2),
            capacity,
        });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EventDto>())!;
    }

    public static async Task<BookingDto> BookAsync(HttpClient learner, Guid eventId, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/events/{eventId}/bookings");
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        var response = await learner.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BookingDto>())!;
    }
}
