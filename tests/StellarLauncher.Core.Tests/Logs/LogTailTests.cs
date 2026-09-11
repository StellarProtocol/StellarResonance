using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Logs;
using Xunit;

public class LogTailTests
{
    [Theory]
    [InlineData("[Info   :   BepInEx] Loading [Stellar Framework 2.7.4]", LogLevel.Info, "BepInEx", "Loading [Stellar Framework 2.7.4]")]
    [InlineData("[Warning:   Stellar] [PluginRegistry] duplicate plugin id 'combatmeter'; second registration ignored.", LogLevel.Warning, "Stellar", "[PluginRegistry] duplicate plugin id 'combatmeter'; second registration ignored.")]
    [InlineData("[Message:   Stellar] [Stellar] diagnostics=ON", LogLevel.Message, "Stellar", "[Stellar] diagnostics=ON")]
    [InlineData("[Fatal  :   BepInEx] Unhandled exception", LogLevel.Fatal, "BepInEx", "Unhandled exception")]
    [InlineData("   at Foo.Bar()", LogLevel.Unknown, "", "   at Foo.Bar()")]
    public void Parse_levels_and_sources(string raw, LogLevel level, string source, string text)
    {
        var l = LogTail.Parse(raw);
        Assert.Equal(level, l.Level); Assert.Equal(source, l.Source); Assert.Equal(text, l.Text); Assert.Equal(raw, l.Raw);
    }

    [Fact]
    public void ReadLast_returns_the_tail_and_empty_for_missing()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/g/BepInEx/LogOutput.log", new MockFileData("[Info   :   BepInEx] one\n[Info   :   BepInEx] two\n[Error  :   Stellar] three\n"));
        var last = LogTail.ReadLast(fs, "/g/BepInEx/LogOutput.log", 2);
        Assert.Equal(new[] { "two", "three" }, last.Select(l => l.Text).ToArray());
        Assert.Equal(LogLevel.Error, last[1].Level);
        Assert.Empty(LogTail.ReadLast(fs, "/g/nope.log", 5));
    }
}
