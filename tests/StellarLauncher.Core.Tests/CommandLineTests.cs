using StellarLauncher.Core.Services;

public class CommandLineTests
{
    [Fact] public void Plain_splits_on_spaces() =>
        Assert.Equal(new[] { "a", "b", "c" }, CommandLine.Split("a b c"));

    [Fact] public void Collapses_runs_of_whitespace() =>
        Assert.Equal(new[] { "a", "b" }, CommandLine.Split("  a   b  "));

    [Fact] public void Double_quotes_keep_spaces() =>
        Assert.Equal(new[] { "a b", "c" }, CommandLine.Split("\"a b\" c"));

    [Fact] public void Single_quotes_keep_spaces() =>
        Assert.Equal(new[] { "a b", "c" }, CommandLine.Split("'a b' c"));

    [Fact] public void Wrapper_with_flags() =>
        Assert.Equal(new[] { "gamescope", "-f", "--" }, CommandLine.Split("gamescope -f --"));

    [Fact] public void Empty_and_null_give_empty() =>
        Assert.Empty(CommandLine.Split(""));

    [Fact] public void Null_gives_empty() =>
        Assert.Empty(CommandLine.Split(null));
}
