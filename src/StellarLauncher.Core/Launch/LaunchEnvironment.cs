using System;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Launch;

/// <summary>Everything about the machine the orchestrator needs — one seam for tests.</summary>
public sealed class LaunchEnvironment
{
    private readonly IPlatformInfo _platform;
    private readonly IGameDetector _detector;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public IFileSystem Fs { get; }
    public Func<bool> MangoHudUserConfig { get; init; } = MangoHud.UserConfigExists;

    public LaunchEnvironment(IFileSystem fs, IPlatformInfo platform, IGameDetector detector,
        Func<DateTimeOffset>? now = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        Fs = fs; _platform = platform; _detector = detector;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
    }

    public bool IsWindows => _platform.IsWindows;
    public string? UmuRun => _platform.IsWindows ? null : _detector.DetectUmu();
    public DateTimeOffset Now => _now();
    public Task DelayAsync(TimeSpan d, CancellationToken ct) => _delay(d, ct);
}
