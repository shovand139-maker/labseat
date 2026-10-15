using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LabSeat.Api.Tests;

public sealed class SqlErrorMappingTests
{
    [Theory]
    [InlineData(50001, 404)]
    [InlineData(50003, 409)]
    [InlineData(50004, 409)]
    [InlineData(50005, 404)]
    [InlineData(50007, 403)]
    [InlineData(50008, 422)]
    [InlineData(2627, 409)]
    [InlineData(33504, 403)]
    public void Known_errors_map_to_http_status(int number, int status) =>
        Assert.Equal(status, SqlErrors.Map(number)!.Status);

    [Fact]
    public void Engine_messages_for_duplicates_are_never_exposed() =>
        Assert.False(SqlErrors.Map(2601)!.ExposeMessage);

    [Fact]
    public void Unknown_errors_are_not_mapped() => Assert.Null(SqlErrors.Map(1205));
}

public sealed class CreateEventValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private static CreateEventRequest Valid() =>
        new("AKS bootcamp", "aks-fundamentals", Now.AddDays(1), Now.AddDays(1).AddHours(3), 20);

    [Fact]
    public void A_valid_request_has_no_errors() => Assert.Empty(Valid().Validate(Now));

    [Fact]
    public void Each_rule_reports_its_own_field()
    {
        var errors = (Valid() with { Title = "A", Capacity = 0, StartsAt = Now.AddMinutes(-5), EndsAt = Now.AddMinutes(55) }).Validate(Now);
        Assert.Equal(new[] { "capacity", "startsAt", "title" }, errors.Keys.Order());
    }

    [Fact]
    public void Events_end_after_they_start_and_last_at_most_twelve_hours()
    {
        Assert.Contains("endsAt", (Valid() with { EndsAt = Now.AddDays(1) }).Validate(Now).Keys);
        Assert.Contains("endsAt", (Valid() with { EndsAt = Now.AddDays(1).AddHours(13) }).Validate(Now).Keys);
    }
}

public sealed class MigratorTests
{
    [Fact]
    public void Scripts_are_embedded_and_ordered() =>
        Assert.Equal(
            new[] { "001_schema.sql", "002_row_level_security.sql", "003_procedures.sql", "004_seed_demo.sql" },
            Migrator.Scripts().Select(s => s.Name));

    [Fact]
    public void Batches_split_only_on_standalone_go_lines()
    {
        var batches = Migrator.SplitBatches("SELECT 'GO';\nGO\nSELECT 2;\n  go  \nSELECT 3; -- GOTO is fine");
        Assert.Equal(3, batches.Count);
        Assert.Equal("SELECT 'GO';", batches[0]);
    }
}

public sealed class DevTokenTests
{
    private static IConfiguration Config(string key) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:DevSigningKey"] = key }).Build();

    [Fact]
    public void Tokens_carry_tenant_and_role_claims()
    {
        var user = new UserRow { UserId = Guid.NewGuid(), TenantId = Guid.NewGuid(), Role = "learner", Email = "a@b.example", DisplayName = "A" };
        var token = new JsonWebToken(DevTokens.Create(Config(ApiFactory.SigningKey), user));

        Assert.Equal(user.TenantId.ToString(), token.GetClaim("tid").Value);
        Assert.Equal("learner", token.GetClaim("role").Value);
        Assert.Equal(DevTokens.Issuer, token.Issuer);
    }

    [Fact]
    public void Short_signing_keys_are_refused() =>
        Assert.Throws<InvalidOperationException>(() => DevTokens.SigningKey(Config("too-short")));
}
