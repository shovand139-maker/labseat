namespace LabSeat.Api;

public sealed record CreateEventRequest(string Title, string LabTemplate, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity)
{
    /// <summary>Returns field errors keyed by camelCase property name; empty when valid.</summary>
    public Dictionary<string, string[]> Validate(DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        var title = Title?.Trim() ?? "";
        var template = LabTemplate?.Trim() ?? "";

        if (title.Length is < 3 or > 120) errors["title"] = ["Title must be 3 to 120 characters."];
        if (template.Length is < 2 or > 80) errors["labTemplate"] = ["Lab template must be 2 to 80 characters."];
        if (StartsAt <= now) errors["startsAt"] = ["The event must start in the future."];
        if (EndsAt <= StartsAt) errors["endsAt"] = ["The event must end after it starts."];
        else if (EndsAt - StartsAt > TimeSpan.FromHours(12)) errors["endsAt"] = ["An event can last at most 12 hours."];
        if (Capacity is < 1 or > 500) errors["capacity"] = ["Capacity must be between 1 and 500."];

        return errors;
    }
}

public sealed record SetCapacityRequest(int Capacity);

public sealed record DevTokenRequest(string Email);

public sealed record DevTokenResponse(string AccessToken, string TokenType, int ExpiresIn, UserRow User);
