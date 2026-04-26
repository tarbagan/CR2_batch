using System.Collections.ObjectModel;
using System.IO;
using PhotoBatchStudio.App.Models;
using PhotoBatchStudio.App.Services;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Forms = System.Windows.Forms;

namespace PhotoBatchStudio.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".rw2", ".orf", ".raf",
        ".jpg", ".jpeg", ".tif", ".tiff", ".png", ".bmp"
    };

    private readonly IPhotoBatchService _photoBatchService;
    private string? _xmpPresetPath;
    private string _outputFolder = string.Empty;
    private string _description = string.Empty;
    private string _externalAiCommand = string.Empty;
    private string _logText = "Project created. Add files or folders to start.\r\n";
    private string _summary = "No files added.";
    private double _blurThreshold = 120.0;
    private int _jpegQuality = 92;
    private bool _recursive = true;
    private bool _overwriteXmp;
    private bool _detectBlur = true;
    private bool _autoFixBlur = true;
    private bool _useExternalAi;
    private bool _renderRawWithPhotoshop = true;
    private bool _isProcessing;

    public MainWindowViewModel()
        : this(new PhotoBatchService())
    {
    }

    public MainWindowViewModel(IPhotoBatchService photoBatchService)
    {
        _photoBatchService = photoBatchService;
        Files = new ObservableCollection<PhotoFileItem>();

        AddFilesCommand = new RelayCommand(_ => AddFiles(), _ => !_isProcessing);
        AddFolderCommand = new RelayCommand(_ => AddFolder(), _ => !_isProcessing);
        BrowsePresetCommand = new RelayCommand(_ => BrowsePreset(), _ => !_isProcessing);
        BrowseOutputCommand = new RelayCommand(_ => BrowseOutput(), _ => !_isProcessing);
        StartProcessingCommand = new RelayCommand(async _ => await StartProcessingAsync(), _ => CanStartProcessing());
    }

    public ObservableCollection<PhotoFileItem> Files { get; }

    public RelayCommand AddFilesCommand { get; }
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand BrowsePresetCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public RelayCommand StartProcessingCommand { get; }

    public string? XmpPresetPath
    {
        get => _xmpPresetPath;
        set => SetProperty(ref _xmpPresetPath, value);
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (SetProperty(ref _outputFolder, value))
            {
                RefreshCommands();
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string ExternalAiCommand
    {
        get => _externalAiCommand;
        set => SetProperty(ref _externalAiCommand, value);
    }

    public string LogText
    {
        get => _logText;
        set => SetProperty(ref _logText, value);
    }

    public string Summary
    {
        get => _summary;
        set => SetProperty(ref _summary, value);
    }

    public double BlurThreshold
    {
        get => _blurThreshold;
        set => SetProperty(ref _blurThreshold, value);
    }

    public int JpegQuality
    {
        get => _jpegQuality;
        set => SetProperty(ref _jpegQuality, value);
    }

    public bool Recursive
    {
        get => _recursive;
        set => SetProperty(ref _recursive, value);
    }

    public bool OverwriteXmp
    {
        get => _overwriteXmp;
        set => SetProperty(ref _overwriteXmp, value);
    }

    public bool DetectBlur
    {
        get => _detectBlur;
        set => SetProperty(ref _detectBlur, value);
    }

    public bool AutoFixBlur
    {
        get => _autoFixBlur;
        set => SetProperty(ref _autoFixBlur, value);
    }

    public bool UseExternalAi
    {
        get => _useExternalAi;
        set => SetProperty(ref _useExternalAi, value);
    }

    public bool RenderRawWithPhotoshop
    {
        get => _renderRawWithPhotoshop;
        set => SetProperty(ref _renderRawWithPhotoshop, value);
    }

    private void AddFiles()
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "Photo files|*.cr2;*.cr3;*.nef;*.arw;*.dng;*.rw2;*.orf;*.raf;*.jpg;*.jpeg;*.tif;*.tiff;*.png;*.bmp",
            Multiselect = true,
            Title = "Select photo files"
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                AddFile(file);
            }
        }
    }

    private void AddFolder()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select photo folder",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            var files = Directory.EnumerateFiles(
                dialog.SelectedPath,
                "*.*",
                Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            foreach (var file in files.Where(file => SupportedExtensions.Contains(Path.GetExtension(file))))
            {
                AddFile(file);
            }
        }
    }

    private void BrowsePreset()
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "Adobe XMP preset|*.xmp",
            Multiselect = false,
            Title = "Select Adobe XMP preset"
        };

        if (dialog.ShowDialog() == true)
        {
            XmpPresetPath = dialog.FileName;
        }
    }

    private void BrowseOutput()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select output folder",
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            OutputFolder = dialog.SelectedPath;
        }
    }

    private void AddFile(string fullPath)
    {
        if (Files.Any(item => string.Equals(item.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var info = new FileInfo(fullPath);
        Files.Add(new PhotoFileItem
        {
            Name = info.Name,
            FullPath = info.FullName,
            DirectoryPath = info.DirectoryName ?? string.Empty,
            FileType = info.Extension.TrimStart('.').ToUpperInvariant(),
            Status = "Queued"
        });

        UpdateSummary();
        RefreshCommands();
    }

    private async Task StartProcessingAsync()
    {
        if (!CanStartProcessing())
        {
            return;
        }

        _isProcessing = true;
        RefreshCommands();
        LogText += $"Starting batch at {DateTime.Now:G}\r\n";

        try
        {
            var settings = new ProcessingSettings
            {
                XmpPresetPath = XmpPresetPath,
                OutputFolder = OutputFolder,
                Description = Description,
                ExternalAiCommand = ExternalAiCommand,
                BlurThreshold = BlurThreshold,
                JpegQuality = JpegQuality,
                Recursive = Recursive,
                OverwriteXmp = OverwriteXmp,
                DetectBlur = DetectBlur,
                AutoFixBlur = AutoFixBlur,
                UseExternalAi = UseExternalAi,
                RenderRawWithPhotoshop = RenderRawWithPhotoshop
            };

            var result = await _photoBatchService.ProcessAsync(Files.ToList(), settings);
            LogText += string.Join(Environment.NewLine, result) + Environment.NewLine;
        }
        catch (Exception ex)
        {
            LogText += $"Error: {ex.Message}{Environment.NewLine}";
            WpfMessageBox.Show(ex.Message, "Photo Batch Studio", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
        finally
        {
            _isProcessing = false;
            RefreshCommands();
        }
    }

    private bool CanStartProcessing()
    {
        return !_isProcessing && Files.Count > 0 && !string.IsNullOrWhiteSpace(OutputFolder);
    }

    private void UpdateSummary()
    {
        Summary = $"{Files.Count} file(s) queued.";
    }

    private void RefreshCommands()
    {
        AddFilesCommand.RaiseCanExecuteChanged();
        AddFolderCommand.RaiseCanExecuteChanged();
        BrowsePresetCommand.RaiseCanExecuteChanged();
        BrowseOutputCommand.RaiseCanExecuteChanged();
        StartProcessingCommand.RaiseCanExecuteChanged();
    }
}
