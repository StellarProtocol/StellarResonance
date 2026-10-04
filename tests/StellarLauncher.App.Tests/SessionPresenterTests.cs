using Avalonia.Media;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Launch;
using Xunit;

/// <summary>N-1: the sticky dependency notice (R-2) must never hide a launch failure or a Steam handoff —
/// both are reported through their own concrete SessionState arms, which must be checked BEFORE the
/// notice's generic "session isn't busy" fallback. It still shows once the session is Idle or Exited — the
/// states a notice legitimately outlives into.</summary>
public class SessionPresenterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 3, 0, 0, TimeSpan.Zero);
    private static readonly InventorySnapshot Inv = new(true, null, null, Array.Empty<InstalledPlugin>(), Array.Empty<DuplicateSlot>(), 0);

    private static Color Brush(LaunchSession s) => ((SolidColorBrush)SessionPresenter.StateBrush(s, Inv)).Color;

    [Fact]
    public void A_pending_notice_never_hides_a_launch_failure()
    {
        var s = new LaunchSession("c1");
        s.SetDependencyNotice("Added ReShade for Photo Studio");
        s.Begin(T0);
        s.Apply(new FailedEvent("launch failed: runner missing"));

        Assert.Equal("launch failed: runner missing", SessionPresenter.StateLine(s, Inv, true));
        Assert.Equal(Color.Parse("#ff9a9a"), Brush(s));
    }

    [Fact]
    public void A_pending_notice_never_hides_a_steam_handoff()
    {
        var s = new LaunchSession("c1");
        s.SetDependencyNotice("Added ReShade for Photo Studio");
        s.Begin(T0);
        s.Apply(new SteamHandoffEvent());

        Assert.Equal("launched via Steam", SessionPresenter.StateLine(s, Inv, true));
        Assert.Equal(Color.Parse("#54e3a0"), Brush(s));
    }

    // The positive half of the pin: the notice must still show for the states it's actually meant for.
    [Fact]
    public void A_pending_notice_still_shows_while_idle_and_once_exited()
    {
        var s = new LaunchSession("c1");
        s.SetDependencyNotice("Added ReShade for Photo Studio");
        Assert.Equal("Added ReShade for Photo Studio", SessionPresenter.StateLine(s, Inv, true));   // Idle
        Assert.Equal(Color.Parse("#9ec2ff"), Brush(s));

        s.Begin(T0);
        s.Apply(new RunningEvent());
        s.Apply(new ExitedEvent(0));
        Assert.Equal("Added ReShade for Photo Studio", SessionPresenter.StateLine(s, Inv, true));   // Exited
        Assert.Equal(Color.Parse("#9ec2ff"), Brush(s));
    }
}
