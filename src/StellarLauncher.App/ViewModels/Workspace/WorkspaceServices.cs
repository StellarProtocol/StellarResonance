using System;
using System.IO.Abstractions;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels.Dashboard;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels.Workspace;

// A parameter-object bundle (records are exempt from the ≤ 6 ctor-deps guardrail, which targets classes).
// Timer: (period, tick) → a handle whose Dispose stops it. The app passes a DispatcherTimer; tests pass a recording fake.
public sealed record WorkspaceServices(DashboardServices Core, IDoorstopToggle Doorstop, IFileSystem Fs,
    IPlatformInfo Platform, IGameDetector Detector, IGameLocator Locator, IConfirm Confirm,
    Func<TimeSpan, Action, IDisposable> Timer);

public enum WorkspaceTab { Overview, Plugins, Settings, Logs }

public sealed record WorkspaceTabFactories(
    Func<ClientWorkspaceViewModel, object> Overview,
    Func<ClientWorkspaceViewModel, object> Plugins,
    Func<ClientWorkspaceViewModel, object> Settings,
    Func<ClientWorkspaceViewModel, object> Logs);

/// <summary>Stands in for a tab that a later task implements.</summary>
public sealed record TabPlaceholder(string Name);
