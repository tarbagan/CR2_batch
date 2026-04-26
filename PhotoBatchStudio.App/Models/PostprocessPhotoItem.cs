using System.IO;
using System.Windows.Media.Imaging;
using PhotoBatchStudio.App.ViewModels;

namespace PhotoBatchStudio.App.Models;

public sealed class PostprocessPhotoItem : ObservableObject
{
    private bool _isEnabled = true;
    private bool _isSelected;
    private bool _hasProcessedOutput;
    private string _status = "Ready";
    private BitmapImage? _thumbnail;
    private string _outputPath = string.Empty;
    private string _selectedModelPath = string.Empty;
    private string _selectedModelName = string.Empty;

    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool HasProcessedOutput
    {
        get => _hasProcessedOutput;
        set
        {
            if (SetProperty(ref _hasProcessedOutput, value))
            {
                RaisePropertyChanged(nameof(StatusGlyph));
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value ?? string.Empty);
    }

    public string SelectedModelPath
    {
        get => _selectedModelPath;
        set => SetProperty(ref _selectedModelPath, value ?? string.Empty);
    }

    public string SelectedModelName
    {
        get => _selectedModelName;
        set => SetProperty(ref _selectedModelName, value ?? string.Empty);
    }

    public string StatusGlyph => HasProcessedOutput ? "✓" : string.Empty;

    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }
}
