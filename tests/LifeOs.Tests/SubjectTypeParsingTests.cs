using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// Every door reads a type through <see cref="SubjectTypes"/> (code review CR41): any case, stored
/// as the schema spells it, and a Space, Metric or Review only through its own service.
/// </summary>
public sealed class SubjectTypeParsingTests
{
    [Theory]
    [InlineData("goal", "Goal")]
    [InlineData(" TASK ", "Task")]
    [InlineData("Reward", "Reward")]
    public void A_creatable_type_reads_in_any_case(string written, string stored)
        => Assert.Equal(stored, SubjectTypes.ParseCreatable(written));

    [Theory]
    [InlineData("Space")]
    [InlineData("metric")]
    [InlineData("Review")]
    public void A_type_with_its_own_service_isnt_made_as_a_plain_item(string type)
    {
        var refused = Assert.Throws<ArgumentException>(() => SubjectTypes.ParseCreatable(type));
        Assert.Contains("its own page and command", refused.Message);
    }

    [Theory]
    [InlineData("Chore")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_type_is_refused_with_the_types_there_are(string? type)
    {
        var refused = Assert.Throws<ArgumentException>(() => SubjectTypes.ParseCreatable(type));
        Assert.Contains("Expected one of: Value, Area, Goal", refused.Message);
    }

    [Fact]
    public void Every_type_is_canonical_in_any_case()
        => Assert.All(SubjectTypes.All, type => Assert.Equal(type, SubjectTypes.Canonical(type.ToLowerInvariant())));
}
