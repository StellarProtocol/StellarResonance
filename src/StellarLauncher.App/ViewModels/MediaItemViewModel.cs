using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels;

// One tile in a plugin's media gallery. Image tiles open a full-size lightbox; YouTube tiles
// show the video thumbnail and open the watch page in the system browser (never embedded);
// hosted video files ("video") open in the browser too — the launcher has no video decoder.
public partial class MediaItemViewModel : ObservableObject
{
    private readonly HttpClient _http;
    private readonly Action<Bitmap> _openLightbox;
    private readonly string? _youTubeId;
    private byte[]? _imageBytes;   // original bytes kept for the full-size lightbox decode

    public PluginMedia Media { get; }
    public bool IsYouTube { get; }
    public bool IsImage { get; }
    public string? Caption => Media.Caption;
    public bool HasCaption => !string.IsNullOrWhiteSpace(Media.Caption);
    public string PlaceholderLabel => IsImage ? "image" : "▶  video";

    [ObservableProperty] private Bitmap? _thumbnail;
    public bool ShowPlaceholder => Thumbnail is null;
    public bool ShowPlayOverlay => IsYouTube && Thumbnail is not null;

    public MediaItemViewModel(PluginMedia media, HttpClient http, Action<Bitmap> openLightbox)
    {
        Media = media; _http = http; _openLightbox = openLightbox;
        IsYouTube = string.Equals(media.Type, "youtube", StringComparison.OrdinalIgnoreCase);
        IsImage = string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase);
        _youTubeId = IsYouTube ? YouTubeUrl.TryGetVideoId(media.Url) : null;
    }

    partial void OnThumbnailChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(ShowPlayOverlay));
    }

    public async Task LoadAsync()
    {
        try
        {
            var url = IsYouTube
                ? (_youTubeId is { } id ? YouTubeUrl.ThumbnailUrl(id) : null)
                : (IsImage ? Media.Url : null);
            if (url is null) return;   // hosted video / unparseable YouTube link → keep the ▶ placeholder tile
            var bytes = await _http.GetByteArrayAsync(url);
            var bmp = await Task.Run(() =>
            {
                using var ms = new MemoryStream(bytes);
                return Bitmap.DecodeToWidth(ms, 496);   // tile is 248 wide; decode at 2× for crispness
            });
            if (!IsYouTube) _imageBytes = bytes;
            Thumbnail = bmp;
        }
        catch { /* thumbnail unavailable — the placeholder tile stays, click still works for YouTube */ }
    }

    [RelayCommand]
    private void Open()
    {
        if (IsYouTube)
        {
            Services.Browser.Open(_youTubeId is { } id ? YouTubeUrl.WatchUrl(id) : Media.Url);
            return;
        }
        if (!IsImage) { Services.Browser.Open(Media.Url); return; }   // hosted video file
        if (_imageBytes is null) return;
        try
        {
            using var ms = new MemoryStream(_imageBytes);
            _openLightbox(new Bitmap(ms));
        }
        catch { /* undecodable image — ignore the click */ }
    }
}
