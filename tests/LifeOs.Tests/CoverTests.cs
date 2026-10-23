using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// A cover as stored (COVER-1, D15; cover-images plan §3.1, C1): its JSON round-trips, a
/// cover can only point at Unsplash, a hand-edited one reads as none, and the credit links
/// carry the referral parameters Unsplash asks for.
/// </summary>
public sealed class CoverTests
{
    private static readonly DateTimeOffset SetAt = new(2026, 10, 23, 18, 4, 0, TimeSpan.Zero);

    internal static Cover Sample(int positionY = 40) => new(
        Cover.Unsplash,
        "Dwu85P9SOIk",
        "https://images.unsplash.com/photo-1417325384643-aac51acc9e5d?ixid=M3w2NTQ3",
        "#6c7a89",
        6000,
        4000,
        "mountain range under a cloudy sky",
        "Annie Spratt",
        "https://unsplash.com/@anniespratt",
        "https://unsplash.com/photos/Dwu85P9SOIk",
        positionY,
        SetAt);

    [Fact]
    public void A_cover_round_trips_through_its_json()
    {
        var cover = Sample();

        var json = cover.ToJson();
        var read = Cover.Parse(json.ToJsonString());

        Assert.Equal(cover, read);
        Assert.Equal("unsplash", (string?)json["source"]);
        Assert.Equal(40, (int?)json["position_y"]);
        Assert.Equal("2026-10-23T18:04:00Z", (string?)json["set_at"]);
    }

    [Fact]
    public void A_cover_without_a_description_leaves_the_key_out()
    {
        var json = (Sample() with { Alt = null }).ToJson();

        Assert.False(json.ContainsKey("alt"));
        Assert.Null(Cover.Parse(json.ToJsonString())!.Alt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("\"a cover as text\"")]
    [InlineData("[]")]
    public void Anything_but_a_cover_object_reads_as_none(string? json)
        => Assert.Null(Cover.Parse(json));

    [Theory]
    [InlineData("url", "https://example.com/photo.jpg")]
    [InlineData("url", "http://images.unsplash.com/photo-1")]
    [InlineData("url", "https://images.unsplash.com.example.com/photo-1")]
    [InlineData("url", "https://images.unsplash.com@example.com/photo-1")]
    [InlineData("url", "https://images.unsplash.com:8443/photo-1")]
    [InlineData("url", "https://plus.unsplash.com/premium_photo-1")]
    [InlineData("photographer_url", "https://example.com/@anniespratt")]
    [InlineData("photo_url", "javascript:alert(1)")]
    [InlineData("color", "red")]
    [InlineData("photo_id", "../../etc")]
    [InlineData("source", "upload")]
    public void A_hand_edited_cover_pointing_elsewhere_reads_as_none(string field, string value)
    {
        var json = Sample().ToJson();
        json[field] = value;

        Assert.Null(Cover.Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData("width")]
    [InlineData("photographer")]
    [InlineData("position_y")]
    [InlineData("set_at")]
    public void A_cover_missing_a_field_reads_as_none(string field)
    {
        var json = Sample().ToJson();
        json.Remove(field);

        Assert.Null(Cover.Parse(json.ToJsonString()));
    }

    [Fact]
    public void A_field_of_the_wrong_kind_reads_as_none()
    {
        var json = Sample().ToJson();
        json["position_y"] = "40";

        Assert.Null(Cover.Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void A_position_outside_0_to_100_is_refused(int position)
    {
        var ex = Assert.Throws<ArgumentException>(() => Sample().WithPosition(position));
        Assert.Contains("0 (top) to 100 (bottom)", ex.Message);
        Assert.Throws<ArgumentException>(() => (Sample() with { PositionY = position }).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void The_ends_are_positions_too(int position)
        => Assert.Equal(position, Sample().WithPosition(position).PositionY);

    [Fact]
    public void Validate_says_what_is_wrong()
    {
        Assert.Contains("images.unsplash.com",
            Assert.Throws<ArgumentException>(() => (Sample() with { Url = "https://example.com/x.jpg" }).Validate()).Message);
        Assert.Contains("unsplash.com",
            Assert.Throws<ArgumentException>(() => (Sample() with { PhotographerUrl = "https://example.com/@x" }).Validate()).Message);
        Assert.Contains("photographer",
            Assert.Throws<ArgumentException>(() => (Sample() with { Photographer = " " }).Validate()).Message);
        Assert.Contains("#rrggbb",
            Assert.Throws<ArgumentException>(() => (Sample() with { Color = "#abc" }).Validate()).Message);
        Assert.Contains("above zero",
            Assert.Throws<ArgumentException>(() => (Sample() with { Width = 0 }).Validate()).Message);
        Assert.Contains($"{Cover.MaxAlt} characters",
            Assert.Throws<ArgumentException>(() => (Sample() with { Alt = new string('a', Cover.MaxAlt + 1) }).Validate()).Message);
    }

    [Fact]
    public void Credit_links_carry_the_referral_parameters()
    {
        var cover = Sample();

        Assert.Equal("https://unsplash.com/@anniespratt?utm_source=lifeos&utm_medium=referral", cover.PhotographerLink("lifeos"));
        Assert.Equal("https://unsplash.com/photos/Dwu85P9SOIk?utm_source=lifeos&utm_medium=referral", cover.PhotoLink("lifeos"));
        Assert.Equal("https://unsplash.com/?utm_source=lifeos&utm_medium=referral", Cover.UnsplashLink("lifeos"));
        Assert.Equal(
            "https://unsplash.com/@x?a=1&utm_source=my_app&utm_medium=referral",
            Cover.WithReferral("https://unsplash.com/@x?a=1", "my_app"));
    }

    [Theory]
    [InlineData("LifeOS")]
    [InlineData("life os")]
    [InlineData("")]
    [InlineData("a&utm_medium=spam")]
    public void An_app_name_that_cant_be_a_utm_source_is_refused(string appName)
        => Assert.Throws<ArgumentException>(() => Cover.UnsplashLink(appName));

    [Theory]
    [InlineData(SubjectTypes.Value, true)]
    [InlineData(SubjectTypes.Goal, true)]
    [InlineData(SubjectTypes.Project, true)]
    [InlineData(SubjectTypes.Task, false)]
    [InlineData(SubjectTypes.Area, false)]
    [InlineData(SubjectTypes.Habit, false)]
    public void Only_identity_statements_goals_and_projects_have_covers(string type, bool allowed)
        => Assert.Equal(allowed, Cover.Allowed(type));

    [Fact]
    public void The_stored_shape_uses_the_documented_names()
    {
        var keys = Sample().ToJson().Select(pair => pair.Key).ToList();

        Assert.Equal(
            ["source", "photo_id", "url", "color", "width", "height", "alt", "photographer", "photographer_url",
             "photo_url", "position_y", "set_at"],
            keys);
    }
}
