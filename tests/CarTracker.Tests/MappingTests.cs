using CarTracker.Api.Endpoints;
using CarTracker.Api.Services;

namespace CarTracker.Tests;

public class MappingTests
{
    [Theory]
    [InlineData("#Suspension", "suspension")]
    [InlineData("  wheel & tyre ", "wheel_&_tyre")]
    [InlineData("##", null)]
    [InlineData("", null)]
    public void NormalizeTag(string raw, string? expected) => Assert.Equal(expected, Mapping.NormalizeTag(raw));

    [Fact]
    public void Invite_hash_ignores_surrounding_whitespace_and_codes_are_unique()
    {
        var code = InviteCodes.Generate();
        Assert.Equal(InviteCodes.Hash(code), InviteCodes.Hash($" {code}\n"));
        Assert.NotEqual(code, InviteCodes.Generate());
        Assert.DoesNotContain('/', code);
        Assert.DoesNotContain('+', code);
    }
}
