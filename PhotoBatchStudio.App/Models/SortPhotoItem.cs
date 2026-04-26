using System.IO;
using System.Windows.Media.Imaging;
using PhotoBatchStudio.App.ViewModels;

namespace PhotoBatchStudio.App.Models;

public sealed class SortPhotoItem : ObservableObject
{
    private bool _isSelectedForFinal;
    private BitmapImage? _thumbnail;

    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public DateTime LastWriteTime { get; init; }
    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    public bool IsSelectedForFinal
    {
        get => _isSelectedForFinal;
        set => SetProperty(ref _isSelectedForFinal, value);
    }

    public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? string.Empty;
}
