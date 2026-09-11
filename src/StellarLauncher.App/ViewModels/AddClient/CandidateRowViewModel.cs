using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Clients;

namespace StellarLauncher.App.ViewModels.AddClient;

/// <summary>One detected (or browsed) install on the Add client page.</summary>
public sealed partial class CandidateRowViewModel : ObservableObject
{
    public ClientCandidate Candidate { get; }
    public string? AlreadyAddedAs { get; }
    /// <summary>The accent this row will be created with (exposed so later rows can avoid reusing it).</summary>
    public string Accent { get; }
    [ObservableProperty] private bool _selected;
    [ObservableProperty] private string _name;

    public CandidateRowViewModel(ClientCandidate candidate, string? alreadyAddedAs, string accent)
    {
        Candidate = candidate; AlreadyAddedAs = alreadyAddedAs; Accent = accent;
        _name = candidate.ProposedName; _selected = alreadyAddedAs is null;
        AccentBrush = AccentBrushes.Solid(accent);
    }

    public string Path => Candidate.GameMiniDir;
    public string LayoutTag => ClientNaming.LayoutTag(Candidate.Layout);
    public bool HasLayoutTag => LayoutTag.Length > 0;
    public bool IsWinePrefix => Candidate.IsWinePrefix;
    public bool IsAvailable => AlreadyAddedAs is null;
    public string RunnerLine => Candidate.Runner is { } r ? $"runner {System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(r) ?? r)}" : "";
    public string PrefixLine => Candidate.WinePrefix is { } p ? $"prefix {p}" : "";
    public string AlreadyLine => AlreadyAddedAs is { } n ? $"already added as {n}" : "";
    public IBrush AccentBrush { get; }
}
