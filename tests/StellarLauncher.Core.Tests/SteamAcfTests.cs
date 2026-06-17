using StellarLauncher.Core.Services;
using Xunit;

public class SteamAcfTests
{
    private const string Acf = """
        "AppState"
        {
        	"appid"		"3489700"
        	"name"		"Blue Protocol Star Resonance"
        	"installdir"		"Blue Protocol Star Resonance"
        	"StateFlags"		"4"
        }
        """;

    [Fact]
    public void Returns_appid_when_installdir_matches()
        => Assert.Equal("3489700", SteamAcf.AppIdForInstallDir(Acf, "Blue Protocol Star Resonance"));

    [Fact]
    public void Match_is_case_insensitive()
        => Assert.Equal("3489700", SteamAcf.AppIdForInstallDir(Acf, "blue protocol star resonance"));

    [Fact]
    public void Null_when_installdir_differs()
        => Assert.Null(SteamAcf.AppIdForInstallDir(Acf, "Some Other Game"));

    [Theory]
    [InlineData("")]
    [InlineData("not a vdf file")]
    [InlineData("\"AppState\" { \"appid\" \"123\" }")]   // no installdir key
    public void Null_on_malformed_or_incomplete(string text)
        => Assert.Null(SteamAcf.AppIdForInstallDir(text, "Blue Protocol Star Resonance"));
}
