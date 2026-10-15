using System.Net;
using System.Net.Http.Json;

namespace LabSeat.Api.Tests;

[Collection("api")]
public sealed class BookingFlowTests(ApiFactory api)
{
    [Fact]
    public async Task Instructor_creates_an_event_and_learners_see_it()
    {
        var instructor = await api.ClientForAsync(Api.Instructor);
        var created = await Api.CreateEventAsync(instructor, capacity: 5);

        var learner = await api.ClientForAsync(Api.Ananya);
        var events = await learner.GetFromJsonAsync<List<EventDto>>("/api/events");

        var seen = Assert.Single(events!, e => e.EventId == created.EventId);
        Assert.Equal(5, seen.SeatsLeft);
        Assert.Null(seen.MyStatus);
    }

    [Fact]
    public async Task Bookings_are_confirmed_until_full_then_waitlisted_in_order()
    {
        var instructor = await api.ClientForAsync(Api.Instructor);
        var evt = await Api.CreateEventAsync(instructor, capacity: 1);

        var first = await Api.BookAsync(await api.ClientForAsync(Api.Ananya), evt.EventId);
        var second = await Api.BookAsync(await api.ClientForAsync(Api.Kabir), evt.EventId);
        var third = await Api.BookAsync(await api.ClientForAsync(Api.Zoya), evt.EventId);

        Assert.Equal("confirmed", first.Status);
        Assert.Equal(("waitlisted", (int?)1), (second.Status, second.QueuePosition));
        Assert.Equal(("waitlisted", (int?)2), (third.Status, third.QueuePosition));

        var refreshed = await instructor.GetFromJsonAsync<EventDto>($"/api/events/{evt.EventId}");
        Assert.Equal((1, 2, 0), (refreshed!.Confirmed, refreshed.Waitlisted, refreshed.SeatsLeft));
    }

    [Fact]
    public async Task Cancelling_a_confirmed_seat_promotes_the_first_waitlisted_learner()
    {
        var instructor = await api.ClientForAsync(Api.Instructor);
        var evt = await Api.CreateEventAsync(instructor, capacity: 1);
        var ananya = await api.ClientForAsync(Api.Ananya);
        var confirmed = await Api.BookAsync(ananya, evt.EventId);
        var waiting = await Api.BookAsync(await api.ClientForAsync(Api.Kabir), evt.EventId);
        var last = await Api.BookAsync(await api.ClientForAsync(Api.Zoya), evt.EventId);

        var response = await ananya.DeleteAsync($"/api/bookings/{confirmed.BookingId}");
        var result = await response.Content.ReadFromJsonAsync<CancelDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(waiting.BookingId, result!.PromotedBookingId);

        var roster = await instructor.GetFromJsonAsync<List<RosterDto>>($"/api/events/{evt.EventId}/roster");
        Assert.Equal(("confirmed", (int?)null), (roster![0].Status, roster[0].QueuePosition));
        Assert.Equal(waiting.BookingId, roster[0].BookingId);
        Assert.Equal((last.BookingId, "waitlisted", (int?)1), (roster[1].BookingId, roster[1].Status, roster[1].QueuePosition));
    }

    [Fact]
    public async Task Retrying_with_the_same_idempotency_key_returns_the_same_booking()
    {
        var instructor = await api.ClientForAsync(Api.Instructor);
        var evt = await Api.CreateEventAsync(instructor, capacity: 3);
        var learner = await api.ClientForAsync(Api.Ananya);
        var key = Guid.NewGuid().ToString();

        var first = await Api.BookAsync(learner, evt.EventId, key);
        var retry = await Api.BookAsync(learner, evt.EventId, key);
        var doubleClick = await Api.BookAsync(learner, evt.EventId);

        Assert.Equal(first.BookingId, retry.BookingId);
        Assert.Equal(first.BookingId, doubleClick.BookingId);
        var refreshed = await instructor.GetFromJsonAsync<EventDto>($"/api/events/{evt.EventId}");
        Assert.Equal(1, refreshed!.Confirmed);
    }

    [Fact]
    public async Task Raising_capacity_promotes_waitlist_and_lowering_below_confirmed_is_rejected()
    {
        var instructor = await api.ClientForAsync(Api.Instructor);
        var evt = await Api.CreateEventAsync(instructor, capacity: 1);
        await Api.BookAsync(await api.ClientForAsync(Api.Ananya), evt.EventId);
        await Api.BookAsync(await api.ClientForAsync(Api.Kabir), evt.EventId);
        await Api.BookAsync(await api.ClientForAsync(Api.Zoya), evt.EventId);

        var raised = await instructor.PutAsJsonAsync($"/api/events/{evt.EventId}/capacity", new { capacity = 2 });
        var afterRaise = await raised.Content.ReadFromJsonAsync<EventDto>();
        Assert.Equal((2, 1), (afterRaise!.Confirmed, afterRaise.Waitlisted));

        var lowered = await instructor.PutAsJsonAsync($"/api/events/{evt.EventId}/capacity", new { capacity = 1 });
        var problem = await lowered.Content.ReadFromJsonAsync<ProblemDto>();
        Assert.Equal(HttpStatusCode.Conflict, lowered.StatusCode);
        Assert.Equal(50004, problem!.Code);
    }

    [Fact]
    public async Task Learners_cannot_cancel_someone_elses_booking()
    {
        var instructor = await api.ClientForAsync(Api.Instructor);
        var evt = await Api.CreateEventAsync(instructor, capacity: 2);
        var booking = await Api.BookAsync(await api.ClientForAsync(Api.Ananya), evt.EventId);

        var kabir = await api.ClientForAsync(Api.Kabir);
        var response = await kabir.DeleteAsync($"/api/bookings/{booking.BookingId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var roster = await instructor.GetFromJsonAsync<List<RosterDto>>($"/api/events/{evt.EventId}/roster");
        Assert.Equal("confirmed", Assert.Single(roster!).Status);
    }

    [Fact]
    public async Task My_bookings_lists_the_learners_own_bookings_with_queue_position()
    {
        var instructor = await api.ClientForAsync(Api.Instructor);
        var evt = await Api.CreateEventAsync(instructor, capacity: 1);
        await Api.BookAsync(await api.ClientForAsync(Api.Kabir), evt.EventId);
        var zoya = await api.ClientForAsync(Api.Zoya);
        await Api.BookAsync(zoya, evt.EventId);

        var mine = await zoya.GetFromJsonAsync<List<BookingDto>>("/api/me/bookings");

        var booking = Assert.Single(mine!, b => b.EventId == evt.EventId);
        Assert.Equal(("waitlisted", (int?)1), (booking.Status, booking.QueuePosition));
    }
}
