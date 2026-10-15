using System.Net.Http.Json;
using Microsoft.Data.SqlClient;

namespace LabSeat.Api.Tests;

[Collection("api")]
public sealed class ConcurrencyTests(ApiFactory api)
{
    [Fact]
    public async Task Forty_simultaneous_bookings_for_ten_seats_never_overbook()
    {
        // A fresh organization with one instructor and 40 learners.
        var tenantId = Guid.NewGuid();
        var suffix = tenantId.ToString("N")[..8];
        var instructorEmail = $"instructor-{suffix}@load.example";
        var learnerEmails = Enumerable.Range(1, 40).Select(i => $"learner{i}-{suffix}@load.example").ToList();

        await using (var maintenance = await ApiFactory.OpenMaintenanceAsync())
        {
            var tenant = new SqlCommand("INSERT dbo.Tenants (TenantId, Name) VALUES (@id, @name)", maintenance);
            tenant.Parameters.AddWithValue("@id", tenantId);
            tenant.Parameters.AddWithValue("@name", $"Load test {suffix}");
            await tenant.ExecuteNonQueryAsync();

            foreach (var (email, role) in learnerEmails.Select(e => (e, "learner")).Prepend((instructorEmail, "instructor")))
            {
                var user = new SqlCommand(
                    "INSERT dbo.Users (UserId, TenantId, Email, DisplayName, Role) VALUES (NEWID(), @tenant, @email, @email, @role)",
                    maintenance);
                user.Parameters.AddWithValue("@tenant", tenantId);
                user.Parameters.AddWithValue("@email", email);
                user.Parameters.AddWithValue("@role", role);
                await user.ExecuteNonQueryAsync();
            }
        }

        var instructor = await api.ClientForAsync(instructorEmail);
        var evt = await Api.CreateEventAsync(instructor, capacity: 10);
        var learners = new List<HttpClient>();
        foreach (var email in learnerEmails)
        {
            learners.Add(await api.ClientForAsync(email));
        }

        var results = await Task.WhenAll(learners.Select(client => Api.BookAsync(client, evt.EventId)));

        Assert.Equal(10, results.Count(r => r.Status == "confirmed"));
        Assert.Equal(30, results.Count(r => r.Status == "waitlisted"));

        var roster = await instructor.GetFromJsonAsync<List<RosterDto>>($"/api/events/{evt.EventId}/roster");
        Assert.Equal(40, roster!.Count);
        Assert.Equal(Enumerable.Range(1, 30).Select(i => (int?)i), roster.Where(r => r.Status == "waitlisted").Select(r => r.QueuePosition));
    }
}
