using StellarLauncher.Core.Services;
using Xunit;

public class YouTubeUrlTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?list=PL123&v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=42", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/shorts/abc_def-123", "abc_def-123")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("http://www.youtube.com/live/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void Extracts_the_video_id(string url, string expected)
        => Assert.Equal(expected, YouTubeUrl.TryGetVideoId(url));

    [Theory]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/channel/UC123")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Rejects_non_video_links(string url)
        => Assert.Null(YouTubeUrl.TryGetVideoId(url));

    [Fact]
    public void Thumbnail_and_watch_urls_are_derived_from_the_id()
    {
        Assert.Equal("https://img.youtube.com/vi/abc123/hqdefault.jpg", YouTubeUrl.ThumbnailUrl("abc123"));
        Assert.Equal("https://www.youtube.com/watch?v=abc123", YouTubeUrl.WatchUrl("abc123"));
    }
}
