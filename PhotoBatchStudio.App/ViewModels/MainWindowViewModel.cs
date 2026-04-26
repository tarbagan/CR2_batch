using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using PhotoBatchStudio.App.Models;
using PhotoBatchStudio.App.Services;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Forms = System.Windows.Forms;

namespace PhotoBatchStudio.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private enum UiLanguage
    {
        Russian,
        English
    }

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".rw2", ".orf", ".raf",
        ".jpg", ".jpeg", ".tif", ".tiff", ".png", ".bmp"
    };

    private readonly IPhotoBatchService _photoBatchService;
    private readonly List<SortPhotoItem> _selectedSortPhotos = [];

    private string? _xmpPresetPath;
    private string _outputFolder = string.Empty;
    private string _sortFolder = string.Empty;
    private string _finalFolder = string.Empty;
    private string _description = string.Empty;
    private string _externalAiCommand = string.Empty;
    private string _logText = string.Empty;
    private string _summary = string.Empty;
    private double _blurThreshold = 120.0;
    private int _jpegQuality = 92;
    private bool _recursive = true;
    private bool _overwriteXmp;
    private bool _detectBlur = true;
    private bool _autoFixBlur = true;
    private bool _useExternalAi;
    private bool _renderRawWithPhotoshop = true;
    private bool _isProcessing;
    private bool _showSettings;
    private UiLanguage _language = UiLanguage.Russian;
    private PhotoFileItem? _selectedFile;
    private SortPhotoItem? _selectedSortPhoto;
    private double _previewZoom = 1.0;
    private string _sortMode = "name_asc";
    private string _projectFilePath = string.Empty;
    private string _processingProgressText = string.Empty;
    private double _processingProgressValue;
    private double _processingProgressMaximum = 1;
    private BitmapImage? _selectedPreviewImage;
    private double _previewViewportWidth = 900;
    private double _previewViewportHeight = 600;
    private FileSystemWatcher? _sortFolderWatcher;
    private CancellationTokenSource? _sortFolderRefreshCts;

    public MainWindowViewModel()
        : this(new PhotoBatchService())
    {
    }

    public MainWindowViewModel(IPhotoBatchService photoBatchService)
    {
        _photoBatchService = photoBatchService;
        Files = new ObservableCollection<PhotoFileItem>();
        SortPhotos = new ObservableCollection<SortPhotoItem>();
        SortModes = new ObservableCollection<SortModeOption>();

        AddFilesCommand = new RelayCommand(_ => AddFiles(), _ => !_isProcessing);
        AddFolderCommand = new RelayCommand(_ => AddFolder(), _ => !_isProcessing);
        RemoveSelectedCommand = new RelayCommand(_ => RemoveSelected(), _ => !_isProcessing && SelectedFile is not null);
        ClearListCommand = new RelayCommand(_ => ClearList(), _ => !_isProcessing && Files.Count > 0);
        NewProjectCommand = new RelayCommand(_ => NewProject(), _ => !_isProcessing);
        BrowsePresetCommand = new RelayCommand(_ => BrowsePreset(), _ => !_isProcessing);
        BrowseOutputCommand = new RelayCommand(_ => BrowseOutput(), _ => !_isProcessing);
        BrowseSortFolderCommand = new RelayCommand(_ => BrowseSortFolder(), _ => true);
        BrowseFinalFolderCommand = new RelayCommand(_ => BrowseFinalFolder(), _ => true);
        ToggleSettingsCommand = new RelayCommand(_ => ShowSettings = !ShowSettings);
        SwitchToRussianCommand = new RelayCommand(_ => SwitchLanguage(UiLanguage.Russian), _ => _language != UiLanguage.Russian);
        SwitchToEnglishCommand = new RelayCommand(_ => SwitchLanguage(UiLanguage.English), _ => _language != UiLanguage.English);
        StartProcessingCommand = new RelayCommand(async _ => await StartProcessingAsync(), _ => CanStartProcessing());
        LoadSortPhotosCommand = new RelayCommand(_ => LoadSortPhotos(), _ => Directory.Exists(GetActiveSortSourceFolder()));
        RefreshSortPhotosCommand = new RelayCommand(_ => LoadSortPhotos(), _ => Directory.Exists(GetActiveSortSourceFolder()));
        OpenSortFolderCommand = new RelayCommand(_ => OpenFolder(GetActiveSortSourceFolder()), _ => Directory.Exists(GetActiveSortSourceFolder()));
        MarkCurrentForFinalCommand = new RelayCommand(_ => MarkCurrentForFinal(), _ => SelectedSortPhoto is not null);
        UnmarkCurrentForFinalCommand = new RelayCommand(_ => UnmarkCurrentForFinal(), _ => SelectedSortPhoto is not null);
        SelectAllSortPhotosCommand = new RelayCommand(_ => SelectAllSortPhotos(), _ => SortPhotos.Count > 0);
        ClearSortSelectionCommand = new RelayCommand(_ => ClearSortSelection(), _ => SortPhotos.Any(item => item.IsSelectedForFinal));
        DeleteSelectedSortPhotosCommand = new RelayCommand(_ => DeleteSelectedSortPhotos(), _ => _selectedSortPhotos.Count > 0);
        SendToFinalFolderCommand = new RelayCommand(_ => SendToFinalFolder(), _ => CanSendToFinalFolder());
        SaveProjectCommand = new RelayCommand(_ => SaveProject());
        LoadProjectCommand = new RelayCommand(_ => LoadProject());

        InitializeTexts();
        UpdateSortModes();
    }

    public ObservableCollection<PhotoFileItem> Files { get; }
    public ObservableCollection<SortPhotoItem> SortPhotos { get; }
    public ObservableCollection<SortModeOption> SortModes { get; }

    public RelayCommand AddFilesCommand { get; }
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand RemoveSelectedCommand { get; }
    public RelayCommand ClearListCommand { get; }
    public RelayCommand NewProjectCommand { get; }
    public RelayCommand BrowsePresetCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public RelayCommand BrowseSortFolderCommand { get; }
    public RelayCommand BrowseFinalFolderCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public RelayCommand SwitchToRussianCommand { get; }
    public RelayCommand SwitchToEnglishCommand { get; }
    public RelayCommand StartProcessingCommand { get; }
    public RelayCommand LoadSortPhotosCommand { get; }
    public RelayCommand RefreshSortPhotosCommand { get; }
    public RelayCommand OpenSortFolderCommand { get; }
    public RelayCommand MarkCurrentForFinalCommand { get; }
    public RelayCommand UnmarkCurrentForFinalCommand { get; }
    public RelayCommand SelectAllSortPhotosCommand { get; }
    public RelayCommand ClearSortSelectionCommand { get; }
    public RelayCommand DeleteSelectedSortPhotosCommand { get; }
    public RelayCommand SendToFinalFolderCommand { get; }
    public RelayCommand SaveProjectCommand { get; }
    public RelayCommand LoadProjectCommand { get; }

    public PhotoFileItem? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value))
            {
                RefreshCommands();
            }
        }
    }

    public SortPhotoItem? SelectedSortPhoto
    {
        get => _selectedSortPhoto;
        set
        {
            if (SetProperty(ref _selectedSortPhoto, value))
            {
                UpdateSelectedPreviewImage();
                RaisePropertyChanged(nameof(SelectedPreviewImage));
                RaisePropertyChanged(nameof(SelectedPreviewName));
                RefreshCommands();
            }
        }
    }

    public BitmapImage? SelectedPreviewImage => _selectedPreviewImage;
    public string SelectedPreviewName => SelectedSortPhoto?.FileName ?? T("\u0424\u043e\u0442\u043e \u043d\u0435 \u0432\u044b\u0431\u0440\u0430\u043d\u043e", "No photo selected");

    public double PreviewZoom
    {
        get => _previewZoom;
        set
        {
            if (SetProperty(ref _previewZoom, Math.Clamp(value, 0.25, 4.0)))
            {
                RaisePropertyChanged(nameof(PreviewDisplayWidth));
                RaisePropertyChanged(nameof(PreviewDisplayHeight));
            }
        }
    }

    public bool ShowSettings
    {
        get => _showSettings;
        set => SetProperty(ref _showSettings, value);
    }

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
                RaisePropertyChanged(nameof(EffectiveSortFolder));
                ResetSortFolderWatcher();
                RefreshCommands();
            }
        }
    }

    public string SortFolder
    {
        get => _sortFolder;
        set
        {
            if (SetProperty(ref _sortFolder, value))
            {
                RaisePropertyChanged(nameof(EffectiveSortFolder));
                ResetSortFolderWatcher();
                RefreshCommands();
            }
        }
    }

    public string EffectiveSortFolder => string.IsNullOrWhiteSpace(SortFolder) ? OutputFolder : SortFolder;

    public string FinalFolder
    {
        get => _finalFolder;
        set
        {
            if (SetProperty(ref _finalFolder, value))
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

    public string SortMode
    {
        get => _sortMode;
        set
        {
            if (SetProperty(ref _sortMode, value))
            {
                ApplySortMode();
            }
        }
    }

    public string CurrentProjectPath => string.IsNullOrWhiteSpace(_projectFilePath)
        ? T("\u041f\u0440\u043e\u0435\u043a\u0442 \u0435\u0449\u0435 \u043d\u0435 \u0441\u043e\u0445\u0440\u0430\u043d\u0451\u043d", "Project is not saved yet")
        : _projectFilePath;

    public string ProcessingProgressText
    {
        get => _processingProgressText;
        set => SetProperty(ref _processingProgressText, value);
    }

    public double ProcessingProgressValue
    {
        get => _processingProgressValue;
        set => SetProperty(ref _processingProgressValue, value);
    }

    public double ProcessingProgressMaximum
    {
        get => _processingProgressMaximum;
        set => SetProperty(ref _processingProgressMaximum, value);
    }

    public int SelectedSortPhotosCount => _selectedSortPhotos.Count;
    public double PreviewDisplayWidth
    {
        get
        {
            var image = _selectedPreviewImage;
            if (image is null || image.PixelWidth <= 0 || image.PixelHeight <= 0)
            {
                return 240;
            }

            var fitScale = Math.Min(
                _previewViewportWidth / image.PixelWidth,
                _previewViewportHeight / image.PixelHeight);
            fitScale = double.IsFinite(fitScale) && fitScale > 0 ? fitScale : 1.0;
            return Math.Max(240, image.PixelWidth * fitScale * PreviewZoom);
        }
    }

    public double PreviewDisplayHeight
    {
        get
        {
            var image = _selectedPreviewImage;
            if (image is null || image.PixelWidth <= 0 || image.PixelHeight <= 0)
            {
                return 180;
            }

            var fitScale = Math.Min(
                _previewViewportWidth / image.PixelWidth,
                _previewViewportHeight / image.PixelHeight);
            fitScale = double.IsFinite(fitScale) && fitScale > 0 ? fitScale : 1.0;
            return Math.Max(180, image.PixelHeight * fitScale * PreviewZoom);
        }
    }

    public string WindowTitle => T("CR2(RAW) Batch Studio", "CR2(RAW) Batch Studio");
    public string AppTitle => T("CR2(RAW) Batch Studio", "CR2(RAW) Batch Studio");
    public string AppSubtitle => T(
        "Windows-\u043f\u0440\u0438\u043b\u043e\u0436\u0435\u043d\u0438\u0435 \u0434\u043b\u044f CR2/RAW: XMP-\u0440\u0435\u043d\u0434\u0435\u0440, \u043e\u0442\u0431\u043e\u0440 \u0438 \u0444\u0438\u043d\u0430\u043b\u044c\u043d\u0430\u044f \u0441\u0435\u043b\u0435\u043a\u0446\u0438\u044f \u0444\u043e\u0442\u043e.",
        "Windows app for CR2/RAW: XMP render, review and final photo selection.");
    public string AddFilesText => T("\u0414\u043e\u0431\u0430\u0432\u0438\u0442\u044c \u0444\u0430\u0439\u043b\u044b", "Add Files");
    public string AddFolderText => T("\u0414\u043e\u0431\u0430\u0432\u0438\u0442\u044c \u043f\u0430\u043f\u043a\u0443", "Add Folder");
    public string RemoveSelectedText => T("\u0423\u0434\u0430\u043b\u0438\u0442\u044c", "Remove");
    public string ClearListText => T("\u041e\u0447\u0438\u0441\u0442\u0438\u0442\u044c", "Clear");
    public string NewProjectText => T("\u041d\u043e\u0432\u044b\u0439 \u043f\u0440\u043e\u0435\u043a\u0442", "New Project");
    public string SaveProjectText => T("\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c \u043f\u0440\u043e\u0435\u043a\u0442", "Save Project");
    public string LoadProjectText => T("\u0417\u0430\u0433\u0440\u0443\u0437\u0438\u0442\u044c \u043f\u0440\u043e\u0435\u043a\u0442", "Load Project");
    public string StartBatchText => T("\u0420\u0435\u043d\u0434\u0435\u0440", "Render");
    public string QueueTabText => T("\u041e\u0447\u0435\u0440\u0435\u0434\u044c", "Queue");
    public string ReviewTabText => T("\u0421\u043e\u0440\u0442\u0438\u0440\u043e\u0432\u043a\u0430", "Review");
    public string BatchQueueText => T("\u041e\u0447\u0435\u0440\u0435\u0434\u044c \u0438\u0441\u0445\u043e\u0434\u043d\u0438\u043a\u043e\u0432", "Source Queue");
    public string ReviewPanelText => T("\u0421\u043e\u0440\u0442\u0438\u0440\u043e\u0432\u043a\u0430 JPG", "JPG Review");
    public string PreviewText => T("\u041f\u0440\u043e\u0441\u043c\u043e\u0442\u0440", "Preview");
    public string ProcessingLogText => T("\u041b\u043e\u0433", "Log");
    public string SettingsTitleText => T("\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438", "Settings");
    public string CloseSettingsText => T("\u0417\u0430\u043a\u0440\u044b\u0442\u044c", "Close");
    public string XmpPresetText => T("XMP \u043f\u0440\u0435\u0441\u0435\u0442", "XMP preset");
    public string XmpPresetTooltip => T(
        "XMP \u043f\u0440\u0435\u0441\u0435\u0442: \u044d\u0442\u043e .xmp \u0444\u0430\u0439\u043b \u0438\u0437 Adobe Camera Raw / Photoshop. \u041e\u043d \u0441\u043e\u0434\u0435\u0440\u0436\u0438\u0442 \u043d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438 RAW-\u043e\u0431\u0440\u0430\u0431\u043e\u0442\u043a\u0438.",
        "XMP preset: this is a .xmp file created in Adobe Camera Raw / Photoshop. It contains RAW processing settings.");
    public string OutputFolderText => T("\u041f\u0430\u043f\u043a\u0430 Out", "Out folder");
    public string OutputFolderTooltip => T(
        "\u0421\u044e\u0434\u0430 \u0441\u043e\u0445\u0440\u0430\u043d\u044f\u044e\u0442\u0441\u044f JPG \u043f\u043e\u0441\u043b\u0435 \u0440\u0435\u043d\u0434\u0435\u0440\u0430 \u0438\u043b\u0438 \u0444\u0430\u0439\u043b\u044b \u043f\u043e\u0441\u043b\u0435 \u043f\u0430\u043a\u0435\u0442\u043d\u043e\u0439 \u043e\u0431\u0440\u0430\u0431\u043e\u0442\u043a\u0438.",
        "Rendered JPGs and processed files are saved here.");
    public string SortFolderText => T("\u041f\u0430\u043f\u043a\u0430 review (\u043e\u043f\u0446\u0438\u043e\u043d\u0430\u043b\u044c\u043d\u043e)", "Review folder (optional)");
    public string SortFolderHintText => T("\u0415\u0441\u043b\u0438 \u043f\u0443\u0441\u0442\u043e, \u0438\u0441\u043f\u043e\u043b\u044c\u0437\u0443\u0435\u0442\u0441\u044f Out.", "If empty, the Out folder is used.");
    public string FinalFolderText => T("\u0424\u0438\u043d\u0430\u043b\u044c\u043d\u0430\u044f \u043f\u0430\u043f\u043a\u0430", "Final folder");
    public string FinalFolderTooltip => T(
        "\u0412 \u044d\u0442\u0443 \u043f\u0430\u043f\u043a\u0443 \u043a\u043e\u043f\u0438\u0440\u0443\u044e\u0442\u0441\u044f \u0442\u043e\u043b\u044c\u043a\u043e \u043e\u0442\u043e\u0431\u0440\u0430\u043d\u043d\u044b\u0435 \u0444\u0438\u043d\u0430\u043b\u044c\u043d\u044b\u0435 JPG \u043f\u043e\u0441\u043b\u0435 review.",
        "Only selected final JPGs are copied here after review.");
    public string JpegDescriptionText => T("\u041e\u043f\u0438\u0441\u0430\u043d\u0438\u0435 JPG", "JPG description");
    public string ExternalAiCommandText => T("\u0412\u043d\u0435\u0448\u043d\u044f\u044f AI \u043a\u043e\u043c\u0430\u043d\u0434\u0430", "External AI command");
    public string ExternalAiCommandTooltip => T("\u041f\u0440\u0438\u043c\u0435\u0440: realesrgan-ncnn-vulkan.exe -i {input} -o {output}", "Example: realesrgan-ncnn-vulkan.exe -i {input} -o {output}");
    public string BlurThresholdText => T("\u041f\u043e\u0440\u043e\u0433 \u0440\u0430\u0437\u043c\u044b\u0442\u0438\u044f", "Blur threshold");
    public string JpegQualityText => T("\u041a\u0430\u0447\u0435\u0441\u0442\u0432\u043e JPEG", "JPEG quality");
    public string BrowseText => T("\u041e\u0431\u0437\u043e\u0440", "Browse");
    public string RecursiveText => T("\u0421\u043a\u0430\u043d\u0438\u0440\u043e\u0432\u0430\u0442\u044c \u043f\u043e\u0434\u043f\u0430\u043f\u043a\u0438", "Scan folders recursively");
    public string OverwriteXmpText => T("\u041f\u0435\u0440\u0435\u0437\u0430\u043f\u0438\u0441\u044b\u0432\u0430\u0442\u044c XMP sidecar", "Overwrite existing XMP sidecars");
    public string DetectBlurText => T("\u041e\u043f\u0440\u0435\u0434\u0435\u043b\u044f\u0442\u044c \u0440\u0430\u0437\u043c\u044b\u0442\u0438\u0435", "Detect blur automatically");
    public string AutoFixBlurText => T("\u0411\u0430\u0437\u043e\u0432\u0430\u044f \u043a\u043e\u0440\u0440\u0435\u043a\u0446\u0438\u044f blur", "Auto-fix blurry raster images");
    public string RenderRawText => T("\u0420\u0435\u043d\u0434\u0435\u0440\u0438\u0442\u044c RAW \u0432 JPG \u0447\u0435\u0440\u0435\u0437 Photoshop", "Render RAW to JPG via Photoshop");
    public string UseExternalAiText => T("\u0418\u0441\u043f\u043e\u043b\u044c\u0437\u043e\u0432\u0430\u0442\u044c \u0432\u043d\u0435\u0448\u043d\u044e\u044e AI \u043a\u043e\u043c\u0430\u043d\u0434\u0443", "Use external AI command");
    public string NameColumnText => T("\u0418\u043c\u044f", "Name");
    public string FolderColumnText => T("\u041f\u0430\u043f\u043a\u0430", "Folder");
    public string TypeColumnText => T("\u0422\u0438\u043f", "Type");
    public string StatusColumnText => T("\u0421\u0442\u0430\u0442\u0443\u0441", "Status");
    public string LanguageText => T("\u042f\u0437\u044b\u043a", "Language");
    public string LoadJpgsText => T("\u0417\u0430\u0433\u0440\u0443\u0437\u0438\u0442\u044c JPG", "Load JPGs");
    public string RefreshListText => T("\u041e\u0431\u043d\u043e\u0432\u0438\u0442\u044044c", "Refresh");
    public string RefreshListTooltip => T(
        "\u041f\u0435\u0440\u0435\u0447\u0438\u0442\u0430\u0442\u044c JPG \u0438\u0437 \u043f\u0430\u043f\u043a\u0438 review/Out. \u041f\u043e\u043b\u0435\u0437\u043d\u043e, \u0435\u0441\u043b\u0438 \u0444\u0430\u0439\u043b\u044b \u0431\u044b\u043b\u0438 \u0443\u0434\u0430\u043b\u0435\u043d\u044b \u0438\u043b\u0438 \u0434\u043e\u0431\u0430\u0432\u043b\u0435\u043d\u044b \u0432\u043d\u0435 \u043f\u0440\u043e\u0433\u0440\u0430\u043c\u043c\u044b.",
        "Reload JPGs from the review/Out folder. Useful if files were deleted or added outside the app.");
    public string OpenFolderText => T("\u041e\u0442\u043a\u0440\u044b\u0442\u044c \u043f\u0430\u043f\u043a\u0443", "Open Folder");
    public string SendToFinalText => T("\u0412 \u0444\u0438\u043d\u0430\u043b\u044c\u043d\u0443\u044e \u043f\u0430\u043f\u043a\u0443", "Send To Final");
    public string SendToFinalTooltip => T(
        "\u041a\u043e\u043f\u0438\u0440\u043e\u0432\u0430\u0442\u044c \u043e\u0442\u043c\u0435\u0447\u0435\u043d\u043d\u044b\u0435 \u043a\u0430\u0434\u0440\u044b \u0432 \u0444\u0438\u043d\u0430\u043b\u044c\u043d\u0443\u044e \u043f\u0430\u043f\u043a\u0443. \u042d\u0442\u043e \u0438\u0442\u043e\u0433\u043e\u0432\u044b\u0439 \u0448\u0430\u0433 \u043e\u0442\u0431\u043e\u0440\u0430.",
        "Copy selected final photos into the final folder. This is the last review step.");
    public string SelectAllText => T("\u0412\u044b\u0431\u0440\u0430\u0442\u044c \u0432\u0441\u0435", "Select All");
    public string ClearSelectionText => T("\u0421\u043d\u044f\u0442\u044c \u0432\u044b\u0431\u043e\u0440", "Clear Selection");
    public string KeepText => T("\u041e\u0442\u043c\u0435\u0442\u0438\u0442\u044c \u043a\u0430\u043a \u0444\u0438\u043d\u0430\u043b", "Mark For Final");
    public string UnkeepText => T("\u0423\u0431\u0440\u0430\u0442\u044c \u0438\u0437 \u0444\u0438\u043d\u0430\u043b\u0430", "Remove From Final");
    public string RemoveFromReviewText => T("\u0423\u0431\u0440\u0430\u0442\u044c \u0438\u0437 review", "Remove From Review");
    public string ZoomText => T("\u0417\u0443\u043c", "Zoom");
    public string SortModeText => T("\u0421\u043e\u0440\u0442\u0438\u0440\u043e\u0432\u043a\u0430", "Sort");
    public string CurrentProjectText => T("\u0422\u0435\u043a\u0443\u0449\u0438\u0439 \u043f\u0440\u043e\u0435\u043a\u0442", "Current project");
    public string FinalSelectionCountText => T($"\u041e\u0442\u043e\u0431\u0440\u0430\u043d\u043e: {SortPhotos.Count(item => item.IsSelectedForFinal)}", $"Selected: {SortPhotos.Count(item => item.IsSelectedForFinal)}");
    public string QueueProgressText => T("\u041f\u0440\u043e\u0433\u0440\u0435\u0441\u0441", "Progress");
    public string ReviewSelectionText => T($"\u0412\u044b\u0431\u0440\u0430\u043d\u043e \u0432 review: {SelectedSortPhotosCount}", $"Selected in review: {SelectedSortPhotosCount}");

    private void InitializeTexts()
    {
        LogText = T("\u041f\u0440\u043e\u0435\u043a\u0442 \u0441\u043e\u0437\u0434\u0430\u043d. \u0414\u043e\u0431\u0430\u0432\u044c\u0442\u0435 \u0444\u0430\u0439\u043b\u044b \u0438\u043b\u0438 \u043f\u0430\u043f\u043a\u0438 \u0434\u043b\u044f \u043d\u0430\u0447\u0430\u043b\u0430.\r\n", "Project created. Add files or folders to start.\r\n");
        ProcessingProgressText = T("\u041f\u0440\u043e\u0433\u0440\u0435\u0441\u0441 \u0435\u0449\u0451 \u043d\u0435 \u0437\u0430\u043f\u0443\u0449\u0435\u043d.", "Progress has not started yet.");
        UpdateSummary();
    }

    private string T(string ru, string en) => _language == UiLanguage.Russian ? ru : en;

    private void SwitchLanguage(UiLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        UpdateSortModes();
        RaiseAllTextProperties();
        UpdateSummary();
        RefreshCommands();
    }

    private void RaiseAllTextProperties()
    {
        string[] names =
        [
            nameof(WindowTitle), nameof(AppTitle), nameof(AppSubtitle), nameof(AddFilesText), nameof(AddFolderText),
            nameof(RemoveSelectedText), nameof(ClearListText), nameof(NewProjectText), nameof(StartBatchText), nameof(BatchQueueText),
            nameof(ReviewPanelText), nameof(PreviewText), nameof(ProcessingLogText), nameof(SettingsTitleText),
            nameof(XmpPresetText), nameof(OutputFolderText), nameof(SortFolderText), nameof(SortFolderHintText), nameof(FinalFolderText),
            nameof(XmpPresetTooltip), nameof(OutputFolderTooltip), nameof(FinalFolderTooltip),
            nameof(JpegDescriptionText), nameof(ExternalAiCommandText), nameof(ExternalAiCommandTooltip),
            nameof(BlurThresholdText), nameof(JpegQualityText), nameof(BrowseText), nameof(RecursiveText),
            nameof(OverwriteXmpText), nameof(DetectBlurText), nameof(AutoFixBlurText), nameof(RenderRawText),
            nameof(UseExternalAiText), nameof(NameColumnText), nameof(FolderColumnText), nameof(TypeColumnText),
            nameof(StatusColumnText), nameof(SaveProjectText), nameof(LoadProjectText), nameof(LanguageText),
            nameof(LoadJpgsText), nameof(RefreshListText), nameof(RefreshListTooltip), nameof(OpenFolderText), nameof(SendToFinalText), nameof(SendToFinalTooltip), nameof(SelectAllText),
            nameof(ClearSelectionText), nameof(KeepText), nameof(UnkeepText), nameof(RemoveFromReviewText), nameof(ZoomText),
            nameof(SortModeText), nameof(CurrentProjectText), nameof(CurrentProjectPath), nameof(FinalSelectionCountText),
            nameof(QueueTabText), nameof(ReviewTabText), nameof(CloseSettingsText), nameof(EffectiveSortFolder),
            nameof(QueueProgressText), nameof(ReviewSelectionText), nameof(SelectedPreviewName)
            , nameof(PreviewDisplayWidth), nameof(PreviewDisplayHeight)
        ];

        foreach (var name in names)
        {
            RaisePropertyChanged(name);
        }
    }

    private void UpdateSortModes()
    {
        SortModes.Clear();
        SortModes.Add(new SortModeOption("name_asc", T("\u0418\u043c\u044f \u2191", "Name \u2191")));
        SortModes.Add(new SortModeOption("name_desc", T("\u0418\u043c\u044f \u2193", "Name \u2193")));
        SortModes.Add(new SortModeOption("date_new", T("\u041d\u043e\u0432\u044b\u0435 \u0441\u043d\u0430\u0447\u0430\u043b\u0430", "Newest first")));
        SortModes.Add(new SortModeOption("date_old", T("\u0421\u0442\u0430\u0440\u044b\u0435 \u0441\u043d\u0430\u0447\u0430\u043b\u0430", "Oldest first")));
        RaisePropertyChanged(nameof(SortModes));
    }

    private void AddFiles()
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "Photo files|*.cr2;*.cr3;*.nef;*.arw;*.dng;*.rw2;*.orf;*.raf;*.jpg;*.jpeg;*.tif;*.tiff;*.png;*.bmp",
            Multiselect = true,
            Title = T("\u0412\u044b\u0431\u043e\u0440 \u0444\u043e\u0442\u043e\u0433\u0440\u0430\u0444\u0438\u0439", "Select photo files")
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
            Description = T("\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043f\u0430\u043f\u043a\u0443 \u0441 \u0444\u043e\u0442\u043e\u0433\u0440\u0430\u0444\u0438\u044f\u043c\u0438", "Select photo folder"),
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
            Title = T("\u0412\u044b\u0431\u043e\u0440 Adobe XMP \u043f\u0440\u0435\u0441\u0435\u0442\u0430", "Select Adobe XMP preset")
        };

        if (dialog.ShowDialog() == true)
        {
            XmpPresetPath = dialog.FileName;
        }
    }

    private void BrowseOutput()
    {
        using var dialog = BuildFolderDialog(T("\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043f\u0430\u043f\u043a\u0443 Out", "Select Out folder"));
        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            OutputFolder = dialog.SelectedPath;
            ResetSortFolderWatcher();
        }
    }

    private void BrowseSortFolder()
    {
        using var dialog = BuildFolderDialog(T("\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043f\u0430\u043f\u043a\u0443 \u0434\u043b\u044f \u0441\u043e\u0440\u0442\u0438\u0440\u043e\u0432\u043a\u0438", "Select review folder"));
        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            SortFolder = dialog.SelectedPath;
            ResetSortFolderWatcher();
        }
    }

    private void BrowseFinalFolder()
    {
        using var dialog = BuildFolderDialog(T("\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u0444\u0438\u043d\u0430\u043b\u044c\u043d\u0443\u044e \u043f\u0430\u043f\u043a\u0443", "Select final folder"));
        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            FinalFolder = dialog.SelectedPath;
        }
    }

    private static Forms.FolderBrowserDialog BuildFolderDialog(string description)
    {
        return new Forms.FolderBrowserDialog
        {
            Description = description,
            ShowNewFolderButton = true
        };
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
            Status = T("\u0412 \u043e\u0447\u0435\u0440\u0435\u0434\u0438", "Queued")
        });

        UpdateSummary();
        RefreshCommands();
    }

    private void RemoveSelected()
    {
        if (SelectedFile is null)
        {
            return;
        }

        Files.Remove(SelectedFile);
        SelectedFile = null;
        UpdateSummary();
        RefreshCommands();
    }

    private void ClearList()
    {
        Files.Clear();
        SelectedFile = null;
        UpdateSummary();
        RefreshCommands();
    }

    private void NewProject()
    {
        Files.Clear();
        ClearSortPhotos();
        SelectedFile = null;
        SelectedSortPhoto = null;
        _selectedSortPhotos.Clear();
        RaisePropertyChanged(nameof(SelectedSortPhotosCount));
        RaisePropertyChanged(nameof(ReviewSelectionText));
        XmpPresetPath = string.Empty;
        OutputFolder = string.Empty;
        SortFolder = string.Empty;
        FinalFolder = string.Empty;
        Description = string.Empty;
        ExternalAiCommand = string.Empty;
        PreviewZoom = 1.0;
        ProcessingProgressValue = 0;
        ProcessingProgressMaximum = 1;
        ProcessingProgressText = T("\u041f\u0440\u043e\u0433\u0440\u0435\u0441\u0441 \u0435\u0449\u0451 \u043d\u0435 \u0437\u0430\u043f\u0443\u0449\u0435\u043d.", "Progress has not started yet.");
        _projectFilePath = string.Empty;
        DisposeSortFolderWatcher();
        RaisePropertyChanged(nameof(CurrentProjectPath));
        LogText = T("\u041d\u043e\u0432\u044b\u0439 \u043f\u0440\u043e\u0435\u043a\u0442 \u0441\u043e\u0437\u0434\u0430\u043d.\r\n", "New project created.\r\n");
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
        ProcessingProgressValue = 0;
        ProcessingProgressMaximum = Math.Max(1, Files.Count);
        ProcessingProgressText = T("\u041e\u0436\u0438\u0434\u0430\u043d\u0438\u0435 \u0441\u0442\u0430\u0440\u0442\u0430...", "Waiting to start...");
        RefreshCommands();
        LogText += $"{T("\u0417\u0430\u043f\u0443\u0441\u043a \u043f\u0430\u043a\u0435\u0442\u043d\u043e\u0439 \u043e\u0431\u0440\u0430\u0431\u043e\u0442\u043a\u0438", "Starting batch")} {DateTime.Now:G}\r\n";

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

            var progress = new Progress<ProcessingProgress>(UpdateProcessingProgress);
            var result = await _photoBatchService.ProcessAsync(Files.ToList(), settings, progress);
            LogText += string.Join(Environment.NewLine, result) + Environment.NewLine;
            LoadSortPhotos();
        }
        catch (Exception ex)
        {
            LogText += $"{T("\u041e\u0448\u0438\u0431\u043a\u0430", "Error")}: {ex.Message}{Environment.NewLine}";
            WpfMessageBox.Show(ex.Message, WindowTitle, WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
        finally
        {
            _isProcessing = false;
            if (Files.Count > 0)
            {
                ProcessingProgressValue = ProcessingProgressMaximum;
                ProcessingProgressText = T("\u041e\u0431\u0440\u0430\u0431\u043e\u0442\u043a\u0430 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043d\u0430.", "Processing finished.");
            }
            RefreshCommands();
        }
    }

    private void LoadSortPhotos()
    {
        var sourceFolder = GetActiveSortSourceFolder();
        if (!Directory.Exists(sourceFolder))
        {
            ResetSortFolderWatcher();
            return;
        }

        var rememberedSelection = SortPhotos.Where(item => item.IsSelectedForFinal).Select(item => item.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentPreviewPath = SelectedSortPhoto?.FilePath;
        ClearSortPhotos();

        var files = Directory.EnumerateFiles(sourceFolder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(file =>
            {
                var extension = Path.GetExtension(file);
                return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            var item = new SortPhotoItem
            {
                FilePath = file,
                FileName = info.Name,
                LastWriteTime = info.LastWriteTime,
                Thumbnail = CreateThumbnail(file),
                IsSelectedForFinal = rememberedSelection.Contains(file)
            };
            item.PropertyChanged += OnSortPhotoPropertyChanged;
            SortPhotos.Add(item);
        }

        ApplySortMode();
        SelectedSortPhoto = SortPhotos.FirstOrDefault(item => string.Equals(item.FilePath, currentPreviewPath, StringComparison.OrdinalIgnoreCase))
            ?? SortPhotos.FirstOrDefault();
        SetSelectedSortPhotos(SelectedSortPhoto is null ? [] : [SelectedSortPhoto]);
        ResetSortFolderWatcher();
        RaisePropertyChanged(nameof(FinalSelectionCountText));
        RefreshCommands();
    }

    private static BitmapImage? CreateThumbnail(string filePath)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(filePath);
            image.DecodePixelWidth = 240;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? CreatePreviewImage(string filePath)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(filePath, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void ApplySortMode()
    {
        IEnumerable<SortPhotoItem> ordered = SortPhotos;

        ordered = SortMode switch
        {
            "name_desc" => SortPhotos.OrderByDescending(item => item.FileName, StringComparer.OrdinalIgnoreCase),
            "date_new" => SortPhotos.OrderByDescending(item => item.LastWriteTime),
            "date_old" => SortPhotos.OrderBy(item => item.LastWriteTime),
            _ => SortPhotos.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
        };

        var sorted = ordered.ToList();
        SortPhotos.Clear();
        foreach (var item in sorted)
        {
            SortPhotos.Add(item);
        }

        RaisePropertyChanged(nameof(FinalSelectionCountText));
    }

    private void MarkCurrentForFinal()
    {
        var targets = GetActiveReviewTargets();
        if (targets.Count == 0)
        {
            return;
        }

        foreach (var photo in targets)
        {
            photo.IsSelectedForFinal = true;
        }

        RaisePropertyChanged(nameof(FinalSelectionCountText));
        RefreshCommands();
    }

    private void UnmarkCurrentForFinal()
    {
        var targets = GetActiveReviewTargets();
        if (targets.Count == 0)
        {
            return;
        }

        foreach (var photo in targets)
        {
            photo.IsSelectedForFinal = false;
        }

        RaisePropertyChanged(nameof(FinalSelectionCountText));
        RefreshCommands();
    }

    private void SelectAllSortPhotos()
    {
        foreach (var photo in SortPhotos)
        {
            photo.IsSelectedForFinal = true;
        }

        RaisePropertyChanged(nameof(FinalSelectionCountText));
        RefreshCommands();
    }

    private void ClearSortSelection()
    {
        foreach (var photo in SortPhotos)
        {
            photo.IsSelectedForFinal = false;
        }

        RaisePropertyChanged(nameof(FinalSelectionCountText));
        RefreshCommands();
    }

    private void DeleteSelectedSortPhotos()
    {
        var targets = _selectedSortPhotos.ToList();
        if (targets.Count == 0)
        {
            return;
        }

        foreach (var photo in targets)
        {
            photo.PropertyChanged -= OnSortPhotoPropertyChanged;
            SortPhotos.Remove(photo);
        }

        _selectedSortPhotos.Clear();
        SelectedSortPhoto = SortPhotos.FirstOrDefault();
        RaisePropertyChanged(nameof(SelectedSortPhotosCount));
        RaisePropertyChanged(nameof(ReviewSelectionText));
        RaisePropertyChanged(nameof(FinalSelectionCountText));
        RefreshCommands();
    }

    private void SendToFinalFolder()
    {
        if (!CanSendToFinalFolder())
        {
            return;
        }

        Directory.CreateDirectory(FinalFolder);
        var selected = SortPhotos.Where(item => item.IsSelectedForFinal).ToList();

        foreach (var item in selected)
        {
            var destination = Path.Combine(FinalFolder, item.FileName);
            File.Copy(item.FilePath, destination, overwrite: true);
        }

        LogText += $"{T("\u0412 \u0444\u0438\u043d\u0430\u043b\u044c\u043d\u0443\u044e \u043f\u0430\u043f\u043a\u0443 \u0441\u043a\u043e\u043f\u0438\u0440\u043e\u0432\u0430\u043d\u043e", "Copied to final folder")}: {selected.Count}\r\n";
        OpenFolder(FinalFolder);
    }

    private bool CanSendToFinalFolder()
    {
        return SortPhotos.Any(item => item.IsSelectedForFinal) && !string.IsNullOrWhiteSpace(FinalFolder);
    }

    private void SaveProject()
    {
        var dialog = new WpfSaveFileDialog
        {
            Filter = "Photo Batch Project|*.pbproj.json",
            FileName = "project.pbproj.json",
            Title = T("\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c \u043f\u0440\u043e\u0435\u043a\u0442", "Save project")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var state = new ProjectState
        {
            Language = _language == UiLanguage.Russian ? "ru" : "en",
            QueueFiles = Files.Select(item => item.FullPath).ToList(),
            XmpPresetPath = XmpPresetPath,
            OutputFolder = OutputFolder,
            SortFolder = SortFolder,
            FinalFolder = FinalFolder,
            Description = Description,
            ExternalAiCommand = ExternalAiCommand,
            BlurThreshold = BlurThreshold,
            JpegQuality = JpegQuality,
            Recursive = Recursive,
            OverwriteXmp = OverwriteXmp,
            DetectBlur = DetectBlur,
            AutoFixBlur = AutoFixBlur,
            UseExternalAi = UseExternalAi,
            RenderRawWithPhotoshop = RenderRawWithPhotoshop,
            SortMode = SortMode,
            FinalSelection = SortPhotos.Where(item => item.IsSelectedForFinal).Select(item => item.FilePath).ToList()
        };

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dialog.FileName, json);
        _projectFilePath = dialog.FileName;
        RaisePropertyChanged(nameof(CurrentProjectPath));
        LogText += $"{T("\u041f\u0440\u043e\u0435\u043a\u0442 \u0441\u043e\u0445\u0440\u0430\u043d\u0451\u043d", "Project saved")}: {_projectFilePath}\r\n";
    }

    private void LoadProject()
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "Photo Batch Project|*.pbproj.json",
            Multiselect = false,
            Title = T("\u0417\u0430\u0433\u0440\u0443\u0437\u0438\u0442\u044c \u043f\u0440\u043e\u0435\u043a\u0442", "Load project")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var state = JsonSerializer.Deserialize<ProjectState>(File.ReadAllText(dialog.FileName));
        if (state is null)
        {
            return;
        }

        Files.Clear();
        foreach (var file in state.QueueFiles.Where(File.Exists))
        {
            AddFile(file);
        }

        SwitchLanguage(state.Language == "en" ? UiLanguage.English : UiLanguage.Russian);
        XmpPresetPath = state.XmpPresetPath;
        OutputFolder = state.OutputFolder;
        SortFolder = state.SortFolder;
        FinalFolder = state.FinalFolder;
        Description = state.Description;
        ExternalAiCommand = state.ExternalAiCommand;
        BlurThreshold = state.BlurThreshold;
        JpegQuality = state.JpegQuality;
        Recursive = state.Recursive;
        OverwriteXmp = state.OverwriteXmp;
        DetectBlur = state.DetectBlur;
        AutoFixBlur = state.AutoFixBlur;
        UseExternalAi = state.UseExternalAi;
        RenderRawWithPhotoshop = state.RenderRawWithPhotoshop;
        SortMode = state.SortMode;

        _projectFilePath = dialog.FileName;
        RaisePropertyChanged(nameof(CurrentProjectPath));

        LoadSortPhotos();
        var selectedPaths = state.FinalSelection.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var photo in SortPhotos)
        {
            photo.IsSelectedForFinal = selectedPaths.Contains(photo.FilePath);
        }

        RaisePropertyChanged(nameof(FinalSelectionCountText));
        LogText += $"{T("\u041f\u0440\u043e\u0435\u043a\u0442 \u0437\u0430\u0433\u0440\u0443\u0436\u0435\u043d", "Project loaded")}: {_projectFilePath}\r\n";
        ResetSortFolderWatcher();
    }

    private string GetActiveSortSourceFolder()
    {
        if (!string.IsNullOrWhiteSpace(SortFolder))
        {
            return SortFolder;
        }

        return OutputFolder;
    }

    private static void OpenFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folderPath,
            UseShellExecute = true
        });
    }

    private bool CanStartProcessing()
    {
        return !_isProcessing && Files.Count > 0 && !string.IsNullOrWhiteSpace(OutputFolder);
    }

    private void UpdateSummary()
    {
        Summary = Files.Count == 0
            ? T("\u0421\u043f\u0438\u0441\u043e\u043a \u0438\u0441\u0445\u043e\u0434\u043d\u0438\u043a\u043e\u0432 \u043f\u0443\u0441\u0442.", "No source files added.")
            : T($"\u0412 \u043e\u0447\u0435\u0440\u0435\u0434\u0438: {Files.Count}. \u0412 \u0444\u0438\u043d\u0430\u043b\u044c\u043d\u043e\u043c \u043e\u0442\u0431\u043e\u0440\u0435: {SortPhotos.Count(item => item.IsSelectedForFinal)}.", $"Queued: {Files.Count}. Final picks: {SortPhotos.Count(item => item.IsSelectedForFinal)}.");

        RaisePropertyChanged(nameof(FinalSelectionCountText));
    }

    private void RefreshCommands()
    {
        AddFilesCommand.RaiseCanExecuteChanged();
        AddFolderCommand.RaiseCanExecuteChanged();
        RemoveSelectedCommand.RaiseCanExecuteChanged();
        ClearListCommand.RaiseCanExecuteChanged();
        NewProjectCommand.RaiseCanExecuteChanged();
        BrowsePresetCommand.RaiseCanExecuteChanged();
        BrowseOutputCommand.RaiseCanExecuteChanged();
        BrowseSortFolderCommand.RaiseCanExecuteChanged();
        BrowseFinalFolderCommand.RaiseCanExecuteChanged();
        SwitchToRussianCommand.RaiseCanExecuteChanged();
        SwitchToEnglishCommand.RaiseCanExecuteChanged();
        StartProcessingCommand.RaiseCanExecuteChanged();
        LoadSortPhotosCommand.RaiseCanExecuteChanged();
        RefreshSortPhotosCommand.RaiseCanExecuteChanged();
        OpenSortFolderCommand.RaiseCanExecuteChanged();
        MarkCurrentForFinalCommand.RaiseCanExecuteChanged();
        UnmarkCurrentForFinalCommand.RaiseCanExecuteChanged();
        SelectAllSortPhotosCommand.RaiseCanExecuteChanged();
        ClearSortSelectionCommand.RaiseCanExecuteChanged();
        DeleteSelectedSortPhotosCommand.RaiseCanExecuteChanged();
        SendToFinalFolderCommand.RaiseCanExecuteChanged();
        UpdateSummary();
    }

    private void OnSortPhotoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SortPhotoItem.IsSelectedForFinal))
        {
            RaisePropertyChanged(nameof(FinalSelectionCountText));
            UpdateSummary();
            SendToFinalFolderCommand.RaiseCanExecuteChanged();
            ClearSortSelectionCommand.RaiseCanExecuteChanged();
        }
    }

    public void SetSelectedSortPhotos(IEnumerable<SortPhotoItem> items)
    {
        _selectedSortPhotos.Clear();
        _selectedSortPhotos.AddRange(items.Where(item => item is not null).Distinct());
        if (_selectedSortPhotos.Count > 0)
        {
            SelectedSortPhoto = _selectedSortPhotos[^1];
        }
        else if (SelectedSortPhoto is not null && !SortPhotos.Contains(SelectedSortPhoto))
        {
            SelectedSortPhoto = null;
        }

        RaisePropertyChanged(nameof(SelectedSortPhotosCount));
        RaisePropertyChanged(nameof(ReviewSelectionText));
        RefreshCommands();
    }

    private List<SortPhotoItem> GetActiveReviewTargets()
    {
        if (_selectedSortPhotos.Count > 0)
        {
            return _selectedSortPhotos.ToList();
        }

        return SelectedSortPhoto is null ? [] : [SelectedSortPhoto];
    }

    private void UpdateSelectedPreviewImage()
    {
        _selectedPreviewImage = SelectedSortPhoto is null ? null : CreatePreviewImage(SelectedSortPhoto.FilePath);
        RaisePropertyChanged(nameof(SelectedPreviewImage));
        RaisePropertyChanged(nameof(PreviewDisplayWidth));
        RaisePropertyChanged(nameof(PreviewDisplayHeight));
    }

    public void UpdatePreviewViewport(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _previewViewportWidth = width;
        _previewViewportHeight = height;
        RaisePropertyChanged(nameof(PreviewDisplayWidth));
        RaisePropertyChanged(nameof(PreviewDisplayHeight));
    }

    public void AdjustPreviewZoom(int mouseWheelDelta)
    {
        var step = mouseWheelDelta > 0 ? 0.15 : -0.15;
        PreviewZoom = Math.Clamp(PreviewZoom + step, 0.25, 6.0);
    }

    private void UpdateProcessingProgress(ProcessingProgress progress)
    {
        ProcessingProgressMaximum = Math.Max(1, progress.Total);
        ProcessingProgressValue = Math.Clamp(progress.Current, 0, progress.Total);
        ProcessingProgressText = T(
            $"\u041e\u0431\u0440\u0430\u0431\u0430\u0442\u044b\u0432\u0430\u0435\u0442\u0441\u044f {progress.Current} \u0438\u0437 {progress.Total}: {progress.CurrentFileName}",
            $"Processing {progress.Current} of {progress.Total}: {progress.CurrentFileName}");
    }

    private void ClearSortPhotos()
    {
        foreach (var photo in SortPhotos)
        {
            photo.PropertyChanged -= OnSortPhotoPropertyChanged;
        }

        SortPhotos.Clear();
    }

    private void ResetSortFolderWatcher()
    {
        DisposeSortFolderWatcher();
        var sourceFolder = GetActiveSortSourceFolder();
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            return;
        }

        _sortFolderWatcher = new FileSystemWatcher(sourceFolder)
        {
            Filter = "*.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _sortFolderWatcher.Created += OnSortFolderChanged;
        _sortFolderWatcher.Deleted += OnSortFolderChanged;
        _sortFolderWatcher.Renamed += OnSortFolderChanged;
    }

    private void DisposeSortFolderWatcher()
    {
        if (_sortFolderWatcher is null)
        {
            return;
        }

        _sortFolderWatcher.EnableRaisingEvents = false;
        _sortFolderWatcher.Created -= OnSortFolderChanged;
        _sortFolderWatcher.Deleted -= OnSortFolderChanged;
        _sortFolderWatcher.Renamed -= OnSortFolderChanged;
        _sortFolderWatcher.Dispose();
        _sortFolderWatcher = null;
    }

    private void OnSortFolderChanged(object sender, FileSystemEventArgs e)
    {
        var extension = Path.GetExtension(e.FullPath);
        if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _sortFolderRefreshCts?.Cancel();
        _sortFolderRefreshCts?.Dispose();
        _sortFolderRefreshCts = new CancellationTokenSource();
        var token = _sortFolderRefreshCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                System.Windows.Application.Current?.Dispatcher.Invoke(() => LoadSortPhotos());
            }
            catch (TaskCanceledException)
            {
            }
        }, token);
    }

    public sealed record SortModeOption(string Value, string Label);
}
