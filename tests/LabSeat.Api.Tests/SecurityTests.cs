using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;

namespace LabSeat.Api.Tests;

[Collection("api")]
public sealed class SecurityTests(ApiFactory api)
{
    [Fact]
    public async Task Requests_without_a_token_get_401()
    {
        var response = await api.CreateClient().GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Learners_cannot_create_events()
    {
        var learner = await api.ClientForAsync(Api.Ananya);
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var response = await learner.PostAsJsonAsync("/api/events", new
        {
            title = "Not allowed", labTemplate = "x1", startsAt = start, endsAt = start.AddHours(1), capacity = 5,
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Another_organizations_event_looks_like_it_does_not_exist()
    {
        var fabrikamInstructor = await api.ClientForAsync(Api.FabrikamInstructor);
        var theirs = await Api.CreateEventAsync(fabrikamInstructor, capacity: 5);

        var contosoInstructor = await api.ClientForAsync(Api.Instructor);
        var contosoLearner = await api.ClientForAsync(Api.Ananya);

        Assert.Equal(HttpStatusCode.NotFound, (await contosoInstructor.GetAsync($"/api/events/{theirs.EventId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await contosoInstructor.GetAsync($"/api/events/{theirs.EventId}/roster")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await contosoLearner.PostAsync($"/api/events/{theirs.EventId}/bookings", null)).StatusCode);
        var list = await contosoLearner.GetFromJsonAsync<List<EventDto>>("/api/events");
        Assert.DoesNotContain(list!, e => e.EventId == theirs.EventId);
    }

    [Fact]
    public async Task Row_level_security_hides_other_tenants_rows_even_without_a_where_clause()
    {
        await using var maintenance = await ApiFactory.OpenMaintenanceAsync();
        await using var contoso = await ApiFactory.OpenTenantAsync(ApiFactory.Contoso);

        var countAll = "SELECT COUNT(*) FROM dbo.LabEvents WHERE TenantId = '22222222-2222-2222-2222-222222222222'";
        var visibleToMaintenance = (int)(await new SqlCommand(countAll, maintenance).ExecuteScalarAsync())!;
        var visibleToContoso = (int)(await new SqlCommand(countAll, contoso).ExecuteScalarAsync())!;

        Assert.True(visibleToMaintenance > 0);
        Assert.Equal(0, visibleToContoso);
    }

    [Fact]
    public async Task Row_level_security_blocks_writing_rows_for_another_tenant()
    {
        await using var contoso = await ApiFactory.OpenTenantAsync(ApiFactory.Contoso);
        var insert = new SqlCommand("""
            INSERT dbo.LabEvents (EventId, TenantId, Title, LabTemplate, StartsAt, EndsAt, Capacity, CreatedBy)
            VALUES (NEWID(), @fabrikam, N'Smuggled', N'x', DATEADD(DAY, 5, SYSUTCDATETIME()), DATEADD(DAY, 6, SYSUTCDATETIME()), 5, @lena)
            """, contoso);
        insert.Parameters.AddWithValue("@fabrikam", ApiFactory.Fabrikam);
        insert.Parameters.AddWithValue("@lena", ApiFactory.Lena);

        var error = await Assert.ThrowsAsync<SqlException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal(33504, error.Number);
    }

    [Fact]
    public async Task Invalid_event_fields_return_422_with_field_errors()
    {
        var instructor = await api.ClientForAsync(Api.Instructor);
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var response = await instructor.PostAsJsonAsync("/api/events", new
        {
            title = "OK title", labTemplate = "aks", startsAt = start, endsAt = start.AddHours(-1), capacity = 0,
        });
        var problem = await response.Content.ReadFromJsonAsync<ProblemDto>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("capacity", problem!.Errors!.Keys);
        Assert.Contains("endsAt", problem.Errors.Keys);
    }

    [Fact]
    public async Task Booking_an_event_that_has_started_is_rejected()
    {
        var eventId = Guid.NewGuid();
        await using (var maintenance = await ApiFactory.OpenMaintenanceAsync())
        {
            var insert = new SqlCommand("""
                INSERT dbo.LabEvents (EventId, TenantId, Title, LabTemplate, StartsAt, EndsAt, Capacity, CreatedBy)
                VALUES (@id, @contoso, N'Already running', N'x', DATEADD(HOUR, -1, SYSUTCDATETIME()), DATEADD(HOUR, 1, SYSUTCDATETIME()), 5,
                        'a0000000-0000-0000-0000-000000000001')
                """, maintenance);
            insert.Parameters.AddWithValue("@id", eventId);
            insert.Parameters.AddWithValue("@contoso", ApiFactory.Contoso);
            await insert.ExecuteNonQueryAsync();
        }

        var learner = await api.ClientForAsync(Api.Ananya);
        var response = await learner.PostAsync($"/api/events/{eventId}/bookings", null);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDto>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(50003, problem!.Code);
    }
}
