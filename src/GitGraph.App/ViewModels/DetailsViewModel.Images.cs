using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Diff;
using GitGraph.Core.Git;

namespace GitGraph.App.ViewModels;

/// <summary>An image file's before and after, shown instead of a text diff.</summary>
public partial class DetailsViewModel
{
    // ---- Images ------------------------------------------------------------------------------

    /// <summary>An image file's before and after, shown side by side instead of a text diff.</summary>
    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _oldImage;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _newImage;

    public bool IsImageDiff => OldImage != null || NewImage != null;
    public string OldImageText => ImageText(OldImage, "Added in this change");
    public string NewImageText => ImageText(NewImage, "Deleted in this change");

    private static string ImageText(Avalonia.Media.Imaging.Bitmap? image, string missing) =>
        image is null ? missing : $"{image.PixelSize.Width} × {image.PixelSize.Height}";

    private static bool IsImage(string path) =>
        FolderTabViewModel.ImageExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    private void SetImages(Avalonia.Media.Imaging.Bitmap? oldImage, Avalonia.Media.Imaging.Bitmap? newImage)
    {
        var (o, n) = (OldImage, NewImage);
        OldImage = oldImage;
        NewImage = newImage;
        o?.Dispose();
        n?.Dispose();
        OnPropertyChanged(nameof(IsImageDiff));
        OnPropertyChanged(nameof(OldImageText));
        OnPropertyChanged(nameof(NewImageText));
    }

    private async Task<bool> LoadImagesAsync(FileChangeViewModel file, CancellationToken ct)
    {
        var (oldBytes, newBytes) = await _repo.GetFileBytesAsync(file.Target, file.Change, ct);
        if (ct.IsCancellationRequested) return true;
        static string Digest(byte[]? b) => b is null ? "-" : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(b));
        var signature = $"{file.Target}|{file.Change.Path}|{Digest(oldBytes)}|{Digest(newBytes)}";
        if (signature == _imageSignature && IsImageDiff) return true;
        static Avalonia.Media.Imaging.Bitmap? Decode(byte[]? bytes)
        {
            if (bytes is null) return null;
            try { return new Avalonia.Media.Imaging.Bitmap(new MemoryStream(bytes)); }
            catch (Exception) { return null; }
        }
        var (oldImage, newImage) = (Decode(oldBytes), Decode(newBytes));
        // Neither side decodes (an SVG, a corrupt file): fall back to the usual binary message.
        if (oldImage is null && newImage is null) return false;
        ResetDiff(null);
        SetImages(oldImage, newImage);
        _imageSignature = signature;
        HasDiff = true;
        return true;
    }
}
