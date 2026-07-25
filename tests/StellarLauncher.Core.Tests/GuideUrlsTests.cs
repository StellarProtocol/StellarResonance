using StellarLauncher.Core.Services;
using Xunit;

public class GuideUrlsTests
{
    private const string Base = "https://cdn.revette.io/plugins/combatmeter/guide.md";

    [Fact]
    public void Relative_paths_resolve_against_the_guides_location()
        => Assert.Equal("https://cdn.revette.io/plugins/combatmeter/media/meter.png",
            GuideUrls.Resolve(Base, "media/meter.png"));

    [Fact]
    public void Absolute_http_urls_pass_through()
        => Assert.Equal("https://example.com/x.png", GuideUrls.Resolve(Base, "https://example.com/x.png"));

    [Fact]
    public void Parent_traversal_stays_inside_the_host()
        => Assert.Equal("https://cdn.revette.io/plugins/other/x.png",
            GuideUrls.Resolve(Base, "../other/x.png"));

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public void Non_http_targets_are_rejected(string target)
        => Assert.Null(GuideUrls.Resolve(Base, target));

    [Fact]
    public void Relative_target_without_a_base_is_rejected()
        => Assert.Null(GuideUrls.Resolve(null, "media/meter.png"));
}
