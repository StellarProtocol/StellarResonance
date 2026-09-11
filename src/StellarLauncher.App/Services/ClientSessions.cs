using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Launch;

namespace StellarLauncher.App.Services;

/// <summary>One <see cref="LaunchSession"/> per client for the launcher's lifetime (spec § 7). Events from the
/// orchestrator are marshalled onto the UI thread via <c>marshal</c> before touching the session.</summary>
public sealed class ClientSessions
{
    private readonly IConfigStore _store;
    private readonly ILaunchOrchestrator _orchestrator;
    private readonly IRunningProcessScanner _scanner;
    private readonly IProcessFactory _processes;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<Action> _marshal;
    private readonly Dictionary<string, LaunchSession> _sessions = new();
    private readonly HashSet<string> _pending = new();  // UI thread only

    public event Action<LaunchSession>? SessionChanged;

    public ClientSessions(IConfigStore store, ILaunchOrchestrator orchestrator, IRunningProcessScanner scanner,
        IProcessFactory processes, Func<DateTimeOffset> now, Action<Action> marshal)
    {
        _store = store; _orchestrator = orchestrator; _scanner = scanner; _processes = processes; _now = now; _marshal = marshal;
    }

    public LaunchSession For(ClientProfile c)
    {
        if (_sessions.TryGetValue(c.Id, out var s)) return s;
        s = new LaunchSession(c.Id);
        s.Changed += x => SessionChanged?.Invoke(x);
        _sessions[c.Id] = s;
        return s;
    }

    public LaunchSession? TryGet(string clientId) => _sessions.GetValueOrDefault(clientId);

    public void Forget(string clientId) => _sessions.Remove(clientId);

    public async Task LaunchAsync(ClientProfile c, IPreLaunchReview review, CancellationToken ct)
    {
        var s = For(c);
        if (!s.CanLaunch || !_pending.Add(c.Id)) return;
        try
        {
            bool proceed;
            try { proceed = await review.ReviewAsync(c, ct); }
            catch (OperationCanceledException) { return; }   // cancelled review = no launch; session stays Idle
            if (!proceed) return;
            s.Begin(_now());
            try
            {
                var outcome = await _orchestrator.LaunchAsync(c, new Relay(e => _marshal(() => s.Apply(e))), ct);
                if (outcome.InteropCount is { } n && n != c.LastInteropCount) PersistInteropCount(c, n);
            }
            catch (OperationCanceledException)
            {
                _marshal(() => s.Apply(new FailedEvent("Launch cancelled")));
            }
            catch (Exception ex)
            {
                _marshal(() => s.Apply(new FailedEvent(ex.Message)));
            }
        }
        finally
        {
            _pending.Remove(c.Id);
        }
    }

    public void Stop(ClientProfile c) => For(c).Stop();

    /// <summary>After a launcher restart: adopt a running game per client by its install root.</summary>
    public void Reattach(IEnumerable<ClientProfile> clients)
    {
        var snapshot = new SnapshotScanner(_scanner.Snapshot());
        foreach (var c in clients)
        {
            if (ProcessReattach.Find(c, snapshot) is not { } pid) continue;
            if (_processes.Attach(pid) is { } p)
            {
                var session = For(c);
                session.AttachRunning(p, _now());
                _ = WatchAttachedExitAsync(session, p);   // reattach bypasses the orchestrator, so wire exit tracking here
            }
        }
    }

    // Without this, a re-attached session stays Running forever after the adopted game closes (CanLaunch never returns).
    private async Task WatchAttachedExitAsync(LaunchSession session, IGameProcess proc)
    {
        try { await proc.WaitForExitAsync(CancellationToken.None); } catch { /* process vanished */ }
        var code = proc.ExitCode;   // getter is guarded; -1 if the code can't be read
        _marshal(() => session.Apply(new ExitedEvent(code)));
    }

    private void PersistInteropCount(ClientProfile c, int count)
    {
        c.LastInteropCount = count;
        var cfg = _store.Load();
        if (cfg.FindById(c.Id) is { } stored) { stored.LastInteropCount = count; _store.Save(cfg); }
    }

    private sealed class Relay(Action<LaunchEvent> onEvent) : IProgress<LaunchEvent>
    {
        public void Report(LaunchEvent e) => onEvent(e);
    }

    private sealed class SnapshotScanner(IReadOnlyList<RunningProcess> procs) : IRunningProcessScanner
    {
        public IReadOnlyList<RunningProcess> Snapshot() => procs;
    }
}
