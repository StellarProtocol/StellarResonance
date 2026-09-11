using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

/// <summary>A running parallel pre-launch script. Killing it ends the script (and its child tree).</summary>
public interface IScriptHandle : IDisposable
{
    bool HasExited { get; }
    void Kill();
}

/// <summary>Runs user pre/post scripts. Injected so the orchestrator's wait-vs-parallel choice is testable
/// without spawning a real shell (the production impl is <see cref="DefaultScriptRunner"/>).</summary>
public interface IScriptRunner
{
    /// <summary>Run the script and wait for it, with a timeout. Returns its exit code, or null if it timed out.</summary>
    Task<int?> RunAsync(string scriptPath, IEnumerable<KeyValuePair<string, string?>> env, TimeSpan timeout, CancellationToken ct = default);

    /// <summary>Start the script without waiting; the returned handle is killed when the game exits. Null if it failed to start.</summary>
    IScriptHandle? Start(string scriptPath, IEnumerable<KeyValuePair<string, string?>> env);
}

/// <summary>Production script runner — both paths go through <see cref="LaunchScripts"/> (real <c>/bin/sh</c>).</summary>
public sealed class DefaultScriptRunner : IScriptRunner
{
    public Task<int?> RunAsync(string scriptPath, IEnumerable<KeyValuePair<string, string?>> env, TimeSpan timeout, CancellationToken ct = default)
        => LaunchScripts.RunAsync(scriptPath, env, timeout, ct);

    public IScriptHandle? Start(string scriptPath, IEnumerable<KeyValuePair<string, string?>> env)
        => LaunchScripts.Start(scriptPath, env);
}
