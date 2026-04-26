using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using PhotoBatchStudio.App.Models;
using PhotoBatchStudio.App.Services;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace PhotoBatchStudio.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly ObservableCollection<PostprocessPhotoItem> _postprocessFiles = [];
    private readonly ObservableCollection<PostprocessModelDescriptor> _availablePostprocessModels = [];
    private readonly ObservableCollection<PostprocessSettingItem> _postprocessSettings = [];
    private readonly List<PostprocessPhotoItem> _selectedPostprocessPhotos = [];
    private readonly ConcurrentDictionary<string, BitmapImage?> _postprocessThumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _postprocessSettingsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _processedPostprocessFiles = new(StringComparer.OrdinalIgnoreCase);
    private List<PostprocessPlanState> _pendingPostprocessPlans = [];
    private HashSet<string> _pendingPostprocessSelectedPaths = [];

    private string _postprocessSourceFolder = string.Empty;
    private string _postprocessLogText = string.Empty;
    private string _postprocessProgressText = string.Empty;
    private string _postprocessStatusText = string.Empty;
    private double _postprocessProgressValue;
    private double _postprocessProgressMaximum = 1;
    private bool _useGpuForModels;
    private bool _autoProcessPostprocess;
    private bool _isProcessingPostprocess;
    private bool _isLoadingPostprocessFiles;
    private BitmapImage? _selectedPostprocessBeforePreviewImage;
    private BitmapImage? _selectedPostprocessAfterPreviewImage;
    private double _selectedPostprocessPreviewZoom = 1.0;
    private double _selectedPostprocessBeforeViewportWidth = 390;
    private double _selectedPostprocessBeforeViewportHeight = 260;
    private double _selectedPostprocessAfterViewportWidth = 390;
    private double _selectedPostprocessAfterViewportHeight = 260;
    private PostprocessModelDescriptor? _selectedPostprocessModel;
    private PostprocessPhotoItem? _selectedPostprocessPhoto;
    private FileSystemWatcher? _postprocessFolderWatcher;
    private CancellationTokenSource? _postprocessFolderRefreshCts;

    public ObservableCollection<PostprocessPhotoItem> PostprocessFiles => _postprocessFiles;
    public ObservableCollection<PostprocessModelDescriptor> AvailablePostprocessModels => _availablePostprocessModels;
    public ObservableCollection<PostprocessSettingItem> PostprocessSettings => _postprocessSettings;

    public RelayCommand BrowsePostprocessSourceCommand { get; private set; } = null!;
    public RelayCommand LoadPostprocessFilesCommand { get; private set; } = null!;
    public RelayCommand RefreshPostprocessFilesCommand { get; private set; } = null!;
    public RelayCommand ReloadPostprocessModelsCommand { get; private set; } = null!;
    public RelayCommand TestSelectedPostprocessModelCommand { get; private set; } = null!;
    public RelayCommand ProcessSelectedPostprocessCommand { get; private set; } = null!;
    public RelayCommand OverwriteSelectedPostprocessCommand { get; private set; } = null!;
    public RelayCommand SelectAllPostprocessCommand { get; private set; } = null!;
    public RelayCommand ClearPostprocessSelectionCommand { get; private set; } = null!;
    public RelayCommand DeleteSelectedPostprocessFilesCommand { get; private set; } = null!;

    public string PostprocessSourceFolder
    {
        get => _postprocessSourceFolder;
        set
        {
            if (SetProperty(ref _postprocessSourceFolder, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(EffectivePostprocessSourceFolder));
                RaisePropertyChanged(nameof(PostprocessSourceFolderTooltip));
                ResetPostprocessFolderWatcher();
                _ = LoadPostprocessFilesAsync(allowAutoProcess: false);
                RefreshPostprocessCommands();
            }
        }
    }

    public string EffectivePostprocessSourceFolder => string.IsNullOrWhiteSpace(PostprocessSourceFolder) ? OutputFolder : PostprocessSourceFolder;
    public string EffectivePostprocessOutputFolder => EffectivePostprocessSourceFolder;

    public bool UseGpuForModels
    {
        get => _useGpuForModels;
        set
        {
            if (SetProperty(ref _useGpuForModels, value))
            {
                RaisePropertyChanged(nameof(PostprocessContextText));
                RefreshPostprocessCommands();
            }
        }
    }

    public bool AutoProcessPostprocess
    {
        get => _autoProcessPostprocess;
        set
        {
            if (SetProperty(ref _autoProcessPostprocess, value))
            {
                RaisePropertyChanged(nameof(PostprocessContextText));
            }
        }
    }

    public string PostprocessLogText
    {
        get => _postprocessLogText;
        set => SetProperty(ref _postprocessLogText, value);
    }

    public string PostprocessProgressText
    {
        get => _postprocessProgressText;
        set => SetProperty(ref _postprocessProgressText, value);
    }

    public double PostprocessProgressValue
    {
        get => _postprocessProgressValue;
        set => SetProperty(ref _postprocessProgressValue, value);
    }

    public double PostprocessProgressMaximum
    {
        get => _postprocessProgressMaximum;
        set => SetProperty(ref _postprocessProgressMaximum, value);
    }

    public string PostprocessStatusText
    {
        get => _postprocessStatusText;
        set => SetProperty(ref _postprocessStatusText, value);
    }

    public PostprocessModelDescriptor? SelectedPostprocessModel
    {
        get => _selectedPostprocessModel;
        set
        {
            if (SetProperty(ref _selectedPostprocessModel, value))
            {
                if (_selectedPostprocessModel is not null)
                {
                    LoadSettingsForSelectedModel();
                    PostprocessStatusText = T($"Модель: {_selectedPostprocessModel.Name}", $"Model: {_selectedPostprocessModel.Name}");
                    AppendPostprocessLog(T(
                        $"[MODEL] {_selectedPostprocessModel.Name} | Profile: {_selectedPostprocessModel.Profile} | File: {_selectedPostprocessModel.FilePath}",
                        $"[MODEL] {_selectedPostprocessModel.Name} | Profile: {_selectedPostprocessModel.Profile} | File: {_selectedPostprocessModel.FilePath}"));
                }
                else
                {
                    PostprocessSettings.Clear();
                    PostprocessStatusText = T("Модель не выбрана", "No model selected");
                }

                RaisePropertyChanged(nameof(PostprocessModelDescriptionText));
                RaisePropertyChanged(nameof(SelectedPostprocessModelPathText));
                RaisePropertyChanged(nameof(PostprocessContextText));
                RefreshPostprocessCommands();
            }
        }
    }

    public void EnsurePostprocessModelsLoaded(bool forceReload = false)
    {
        if (!forceReload && AvailablePostprocessModels.Count > 0)
        {
            if (SelectedPostprocessModel is null || !AvailablePostprocessModels.Any(model => string.Equals(model.FilePath, SelectedPostprocessModel.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedPostprocessModel = AvailablePostprocessModels.FirstOrDefault();
            }

            return;
        }

        LoadPostprocessModels();
    }

    public PostprocessPhotoItem? SelectedPostprocessPhoto
    {
        get => _selectedPostprocessPhoto;
        set
        {
            if (SetProperty(ref _selectedPostprocessPhoto, value))
            {
                UpdateSelectedPostprocessPreviewImage();
                RaisePropertyChanged(nameof(SelectedPostprocessPreviewName));
                RaisePropertyChanged(nameof(SelectedPostprocessModelPathText));
                RaisePropertyChanged(nameof(PostprocessContextText));
                RefreshPostprocessCommands();
            }
        }
    }

    public BitmapImage? SelectedPostprocessBeforePreviewImage => _selectedPostprocessBeforePreviewImage;
    public BitmapImage? SelectedPostprocessAfterPreviewImage => _selectedPostprocessAfterPreviewImage;

    public string SelectedPostprocessPreviewName => SelectedPostprocessPhoto?.FileName ?? T("Фото не выбрано", "No photo selected");
    public string SelectedPostprocessModelPathText => SelectedPostprocessModel?.Name ?? T("Модель не выбрана", "No model selected");

    public double SelectedPostprocessPreviewZoom
    {
        get => _selectedPostprocessPreviewZoom;
        set
        {
            if (SetProperty(ref _selectedPostprocessPreviewZoom, Math.Clamp(value, 0.25, 4.0)))
            {
                RaisePropertyChanged(nameof(SelectedPostprocessBeforePreviewDisplayWidth));
                RaisePropertyChanged(nameof(SelectedPostprocessBeforePreviewDisplayHeight));
                RaisePropertyChanged(nameof(SelectedPostprocessAfterPreviewDisplayWidth));
                RaisePropertyChanged(nameof(SelectedPostprocessAfterPreviewDisplayHeight));
            }
        }
    }

    public double SelectedPostprocessBeforePreviewDisplayWidth
    {
        get
        {
            var image = _selectedPostprocessBeforePreviewImage;
            if (image is null || image.PixelWidth <= 0 || image.PixelHeight <= 0)
            {
                return 240;
            }

            var fitScale = Math.Min(
                _selectedPostprocessBeforeViewportWidth / image.PixelWidth,
                _selectedPostprocessBeforeViewportHeight / image.PixelHeight);
            fitScale = double.IsFinite(fitScale) && fitScale > 0 ? fitScale : 1.0;
            return Math.Max(240, image.PixelWidth * fitScale * SelectedPostprocessPreviewZoom);
        }
    }

    public double SelectedPostprocessBeforePreviewDisplayHeight
    {
        get
        {
            var image = _selectedPostprocessBeforePreviewImage;
            if (image is null || image.PixelWidth <= 0 || image.PixelHeight <= 0)
            {
                return 180;
            }

            var fitScale = Math.Min(
                _selectedPostprocessBeforeViewportWidth / image.PixelWidth,
                _selectedPostprocessBeforeViewportHeight / image.PixelHeight);
            fitScale = double.IsFinite(fitScale) && fitScale > 0 ? fitScale : 1.0;
            return Math.Max(180, image.PixelHeight * fitScale * SelectedPostprocessPreviewZoom);
        }
    }

    public double SelectedPostprocessAfterPreviewDisplayWidth
    {
        get
        {
            var image = _selectedPostprocessAfterPreviewImage;
            if (image is null || image.PixelWidth <= 0 || image.PixelHeight <= 0)
            {
                return 240;
            }

            var fitScale = Math.Min(
                _selectedPostprocessAfterViewportWidth / image.PixelWidth,
                _selectedPostprocessAfterViewportHeight / image.PixelHeight);
            fitScale = double.IsFinite(fitScale) && fitScale > 0 ? fitScale : 1.0;
            return Math.Max(240, image.PixelWidth * fitScale * SelectedPostprocessPreviewZoom);
        }
    }

    public double SelectedPostprocessAfterPreviewDisplayHeight
    {
        get
        {
            var image = _selectedPostprocessAfterPreviewImage;
            if (image is null || image.PixelWidth <= 0 || image.PixelHeight <= 0)
            {
                return 180;
            }

            var fitScale = Math.Min(
                _selectedPostprocessAfterViewportWidth / image.PixelWidth,
                _selectedPostprocessAfterViewportHeight / image.PixelHeight);
            fitScale = double.IsFinite(fitScale) && fitScale > 0 ? fitScale : 1.0;
            return Math.Max(180, image.PixelHeight * fitScale * SelectedPostprocessPreviewZoom);
        }
    }

    public int SelectedPostprocessPhotosCount => PostprocessFiles.Count(item => item.IsEnabled);

    public string PostprocessTabText => T("Постобработка", "Postprocess");
    public string PostprocessPanelText => T("ONNX модели", "ONNX models");
    public string PostprocessSourceFolderText => T("Папка источника", "Source folder");
    public string PostprocessSourceFolderTooltip => T(
        "По умолчанию используется Out. Здесь лежат отсортированные JPG для пакетной ONNX-обработки.",
        "Out is used by default. This folder contains sorted JPGs for batch ONNX processing.");
    public string PostprocessProcessText => T("Обработать", "Process");
    public string PostprocessOverwriteText => T("Перезаписать", "Overwrite");
    public string PostprocessUseGpuText => T("Использовать GPU", "Use GPU");
    public string PostprocessAutoProcessText => T("Автообработка", "Auto process");
    public string PostprocessModelText => T("Модель", "Model");
    public string PostprocessModelDescriptionText => SelectedPostprocessModel?.Description ?? string.Empty;
    public string PostprocessContextText => T(
        $"Источник: {EffectivePostprocessSourceFolder}{Environment.NewLine}Файлов отмечено: {SelectedPostprocessPhotosCount}{Environment.NewLine}Модель: {SelectedPostprocessModel?.Name ?? "не выбрана"}{Environment.NewLine}Профиль: {SelectedPostprocessModel?.Profile ?? "n/a"}{Environment.NewLine}GPU: {(UseGpuForModels ? "вкл" : "выкл")}",
        $"Source: {EffectivePostprocessSourceFolder}{Environment.NewLine}Selected files: {SelectedPostprocessPhotosCount}{Environment.NewLine}Model: {SelectedPostprocessModel?.Name ?? "not selected"}{Environment.NewLine}Profile: {SelectedPostprocessModel?.Profile ?? "n/a"}{Environment.NewLine}GPU: {(UseGpuForModels ? "on" : "off")}");
    public string PostprocessProgressLabelText => T("Прогресс", "Progress");
    public string PostprocessPreviewText => T("Просмотр", "Preview");
    public string PostprocessBeforePreviewText => T("До", "Before");
    public string PostprocessAfterPreviewText => T("После", "After");
    public string PostprocessSelectedFileText => T("Выбранный файл", "Selected file");
    public string PostprocessEnabledColumnText => T("Обрабатывать", "Process");
    public string PostprocessFileColumnText => T("Файл", "File");
    public string PostprocessModelColumnText => T("Модель", "Model");
    public string PostprocessStatusColumnText => T("Статус", "Status");
    public string DeleteFromPostprocessText => T("Удалить из списка", "Delete from list");
    public string PostprocessSelectedCountText => T($"Для ИИ: {SelectedPostprocessPhotosCount}", $"For AI: {SelectedPostprocessPhotosCount}");
    public string ModelsRootFolderText => T("Папка моделей", "Models folder");
    public string ModelsRootFolderTooltip => T("Папка моделей используется по умолчанию из каталога программы.", "Models are loaded from the program folder by default.");
    public string PostprocessOutputFolderText => T("Папка вывода", "Output folder");
    public string PostprocessOutputFolderTooltip => T("Постобработка сохраняет результат рядом с исходным JPG с суффиксом .pp.", "Postprocessing saves output next to the source JPG with a .pp suffix.");
    public string PostprocessLoadText => T("Загрузить JPG", "Load JPG");
    public string PostprocessRefreshText => T("Обновить", "Refresh");
    public string PostprocessModelsRefreshText => T("Обновить модель", "Refresh models");
    public string PostprocessSelectAllText => T("Выбрать всё", "Select all");
    public string PostprocessClearSelectionText => T("Снять выбор", "Clear selection");
    public string PostprocessPlanHintText => T(
        "Установите галочку, чтобы файл участвовал в постобработке. Без галочки файл будет пропущен.",
        "Check the box to include the file in postprocessing. Unchecked files are skipped.");

    public void InitializePostprocessState()
    {
        PostprocessProgressText = T("Постобработка ещё не запущена.", "Postprocess has not started yet.");
        PostprocessStatusText = T("Модель не выбрана", "No model selected");
        LoadPostprocessModels();
        ResetPostprocessFolderWatcher();
    }

    public void ResetPostprocessWorkspace()
    {
        DisposePostprocessFolderWatcher();
        _selectedPostprocessPhotos.Clear();
        _pendingPostprocessPlans.Clear();
        _pendingPostprocessSelectedPaths.Clear();
        _processedPostprocessFiles.Clear();
        _postprocessSettingsCache.Clear();
        _postprocessThumbnailCache.Clear();
        PostprocessFiles.Clear();
        PostprocessSettings.Clear();
        AvailablePostprocessModels.Clear();
        SelectedPostprocessPhoto = null;
        SelectedPostprocessModel = null;
        _selectedPostprocessBeforePreviewImage = null;
        _selectedPostprocessAfterPreviewImage = null;
        SelectedPostprocessPreviewZoom = 1.0;
        PostprocessSourceFolder = string.Empty;
        UseGpuForModels = false;
        AutoProcessPostprocess = false;
        PostprocessProgressValue = 0;
        PostprocessProgressMaximum = 1;
        PostprocessProgressText = T("Постобработка ещё не запущена.", "Postprocess has not started yet.");
        PostprocessStatusText = T("Модель не выбрана", "No model selected");
        RaisePostprocessTextProperties();
        RefreshPostprocessCommands();
    }

    private void LoadPostprocessModels()
    {
        var selectedPath = SelectedPostprocessModel?.FilePath;
        AvailablePostprocessModels.Clear();

        var modelsRoot = ResolveModelsRootFolder();
        var models = new PostprocessService().DiscoverModels(modelsRoot);
        AppendPostprocessLog(T(
            $"[MODELS] Root: {modelsRoot}",
            $"[MODELS] Root: {modelsRoot}"));
        AppendPostprocessLog(T(
            $"[MODELS] Found: {models.Count}",
            $"[MODELS] Found: {models.Count}"));

        foreach (var model in models)
        {
            AvailablePostprocessModels.Add(model);
            AppendPostprocessLog(T(
                $"[MODELS] {model.Name} | {model.Profile} | {model.FilePath}",
                $"[MODELS] {model.Name} | {model.Profile} | {model.FilePath}"));
        }

        if (AvailablePostprocessModels.Count == 0)
        {
            SelectedPostprocessModel = null;
            AppendPostprocessLog(T("[MODELS] No models found.", "[MODELS] No models found."));
            RaisePostprocessTextProperties();
            return;
        }

        SelectedPostprocessModel = FindBestMatchingModel(selectedPath) ?? AvailablePostprocessModels.FirstOrDefault();

        RaisePostprocessTextProperties();
    }

    private PostprocessModelDescriptor? FindBestMatchingModel(string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return null;
        }

        var exact = AvailablePostprocessModels.FirstOrDefault(model => string.Equals(model.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var selectedFileName = Path.GetFileName(selectedPath);
        if (string.IsNullOrWhiteSpace(selectedFileName))
        {
            return null;
        }

        return AvailablePostprocessModels.FirstOrDefault(model =>
            string.Equals(Path.GetFileName(model.FilePath), selectedFileName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(model.Name, Path.GetFileNameWithoutExtension(selectedFileName), StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveModelsRootFolder()
    {
        var candidates = new List<string>();
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            candidates.Add(Path.Combine(current.FullName, "models"));
            current = current.Parent;
        }

        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "models")));
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "models")));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "models"));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "models"));
    }

    private void LoadSettingsForSelectedModel()
    {
        PostprocessSettings.Clear();
        if (SelectedPostprocessModel is null)
        {
            return;
        }

        var modelPath = SelectedPostprocessModel.FilePath;
        if (!_postprocessSettingsCache.TryGetValue(modelPath, out var cached))
        {
            cached = SelectedPostprocessModel.Settings.ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.OrdinalIgnoreCase);
            _postprocessSettingsCache[modelPath] = cached;
        }

        foreach (var setting in SelectedPostprocessModel.Settings)
        {
            var item = new PostprocessSettingItem
            {
                Key = setting.Key,
                Label = setting.Label,
                Kind = setting.Kind,
                Tooltip = setting.Tooltip,
                Minimum = setting.Minimum,
                Maximum = setting.Maximum,
                Options = setting.Options
            };

            if (cached.TryGetValue(setting.Key, out var value))
            {
                item.Value = value;
            }
            else
            {
                item.Value = setting.Value;
                cached[setting.Key] = setting.Value;
            }

            item.PropertyChanged += OnPostprocessSettingPropertyChanged;
            PostprocessSettings.Add(item);
        }

        RaisePostprocessTextProperties();
    }

    private void OnPostprocessSettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PostprocessSettingItem item || e.PropertyName != nameof(PostprocessSettingItem.Value) || SelectedPostprocessModel is null)
        {
            return;
        }

        if (!_postprocessSettingsCache.TryGetValue(SelectedPostprocessModel.FilePath, out var cached))
        {
            cached = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _postprocessSettingsCache[SelectedPostprocessModel.FilePath] = cached;
        }

        cached[item.Key] = item.Value;
    }

    private void BrowsePostprocessSource()
    {
        using var dialog = BuildFolderDialog(T("Выберите папку с JPG для постобработки", "Select JPG folder for postprocess"));
        var owner = GetDialogOwner();
        if (dialog.ShowDialog(owner) == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            PostprocessSourceFolder = dialog.SelectedPath;
        }
    }

    private async Task LoadPostprocessFilesAsync(IEnumerable<string>? explicitFiles = null, bool allowAutoProcess = true)
    {
        var sourceFolder = EffectivePostprocessSourceFolder;
        if (explicitFiles is null && !Directory.Exists(sourceFolder))
        {
            DisposePostprocessFolderWatcher();
            foreach (var item in PostprocessFiles)
            {
                item.PropertyChanged -= OnPostprocessPhotoPropertyChanged;
            }
            PostprocessFiles.Clear();
            _selectedPostprocessPhotos.Clear();
            SelectedPostprocessPhoto = null;
            return;
        }

        _isLoadingPostprocessFiles = true;
        RefreshPostprocessCommands();
        try
        {
            var currentPreviewPath = SelectedPostprocessPhoto?.FilePath;
            var currentSelectionSnapshot = _selectedPostprocessPhotos.Select(item => item.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var item in PostprocessFiles)
            {
                item.PropertyChanged -= OnPostprocessPhotoPropertyChanged;
            }

            PostprocessFiles.Clear();
            _selectedPostprocessPhotos.Clear();

            IEnumerable<string> files = explicitFiles ?? Directory.EnumerateFiles(sourceFolder, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var file in files.Where(file =>
                         (Path.GetExtension(file).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                          Path.GetExtension(file).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) &&
                         !IsDerivedPostprocessOutput(file)))
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                var info = new FileInfo(file);
                var item = new PostprocessPhotoItem
                {
                    FilePath = info.FullName,
                    FileName = info.Name,
                    OutputPath = GetPostprocessOutputPath(info.FullName),
                    IsEnabled = _pendingPostprocessSelectedPaths.Contains(info.FullName)
                };

                item.IsSelected = currentSelectionSnapshot.Contains(info.FullName);
                RefreshPostprocessPhotoState(item);
                item.PropertyChanged += OnPostprocessPhotoPropertyChanged;
                PostprocessFiles.Add(item);
            }

            var restoredSelection = currentSelectionSnapshot.Count > 0;
            if (restoredSelection)
            {
                SetSelectedPostprocessPhotos(PostprocessFiles.Where(item => currentSelectionSnapshot.Contains(item.FilePath)));
            }
            else if (!string.IsNullOrWhiteSpace(currentPreviewPath))
            {
                SelectedPostprocessPhoto = PostprocessFiles.FirstOrDefault(item => string.Equals(item.FilePath, currentPreviewPath, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                SelectedPostprocessPhoto = PostprocessFiles.FirstOrDefault();
            }

            AppendPostprocessLog(T(
                $"[LOAD] Files: {PostprocessFiles.Count}, Source: {sourceFolder}",
                $"[LOAD] Files: {PostprocessFiles.Count}, Source: {sourceFolder}"));
            await LoadPostprocessThumbnailsAsync(PostprocessFiles.ToList());
            ResetPostprocessFolderWatcher();
            RaisePropertyChanged(nameof(PostprocessContextText));
            RefreshPostprocessCommands();

            if (allowAutoProcess && AutoProcessPostprocess && CanProcessPostprocess())
            {
                await ProcessSelectedPostprocessAsync();
            }
        }
        finally
        {
            _isLoadingPostprocessFiles = false;
            RefreshPostprocessCommands();
        }
    }

    private async Task LoadPostprocessThumbnailsAsync(IReadOnlyList<PostprocessPhotoItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2)
        };

        await Parallel.ForEachAsync(items, parallelOptions, async (item, cancellationToken) =>
        {
            if (_postprocessThumbnailCache.TryGetValue(item.FilePath, out var cached))
            {
                if (cached is not null)
                {
                    await dispatcher.InvokeAsync(() => item.Thumbnail = cached);
                }

                return;
            }

            var thumbnail = await Task.Run(() => CreateThumbnail(item.FilePath), cancellationToken);
            _postprocessThumbnailCache[item.FilePath] = thumbnail;
            if (thumbnail is not null)
            {
                await dispatcher.InvokeAsync(() => item.Thumbnail = thumbnail);
            }
        });
    }

    public void SetSelectedPostprocessPhotos(IEnumerable<PostprocessPhotoItem> items)
    {
        _selectedPostprocessPhotos.Clear();
        _selectedPostprocessPhotos.AddRange(items.Where(item => item is not null).Distinct());
        if (_selectedPostprocessPhotos.Count > 0)
        {
            SelectedPostprocessPhoto = _selectedPostprocessPhotos[^1];
        }
        else if (SelectedPostprocessPhoto is not null && !PostprocessFiles.Contains(SelectedPostprocessPhoto))
        {
            SelectedPostprocessPhoto = null;
        }

        RaisePropertyChanged(nameof(SelectedPostprocessPhotosCount));
        RaisePropertyChanged(nameof(PostprocessSelectedCountText));
        RefreshPostprocessCommands();
    }

    private void OnPostprocessPhotoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PostprocessPhotoItem item)
        {
            return;
        }

        if (e.PropertyName is nameof(PostprocessPhotoItem.IsEnabled) or nameof(PostprocessPhotoItem.HasProcessedOutput) or nameof(PostprocessPhotoItem.OutputPath))
        {
            if (ReferenceEquals(item, SelectedPostprocessPhoto))
            {
                UpdateSelectedPostprocessPreviewImage();
            }
        }

        if (e.PropertyName == nameof(PostprocessPhotoItem.IsEnabled))
        {
            _pendingPostprocessSelectedPaths = PostprocessFiles
                .Where(current => current.IsEnabled)
                .Select(current => current.FilePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            RaisePropertyChanged(nameof(PostprocessSelectedCountText));
            RefreshPostprocessCommands();
        }

        if (e.PropertyName is nameof(PostprocessPhotoItem.IsSelected) or nameof(PostprocessPhotoItem.HasProcessedOutput) or nameof(PostprocessPhotoItem.OutputPath))
        {
            RaisePropertyChanged(nameof(PostprocessSelectedCountText));
        }

        if (e.PropertyName is nameof(PostprocessPhotoItem.SelectedModelName))
        {
            RaisePropertyChanged(nameof(SelectedPostprocessModelPathText));
        }
    }

    private void UpdateSelectedPostprocessPreviewImage()
    {
        var photo = SelectedPostprocessPhoto;
        var afterPreviewPath = photo is null ? string.Empty : ResolveAfterPreviewPath(photo);
        _selectedPostprocessBeforePreviewImage = photo is null ? null : CreatePreviewImage(photo.FilePath);
        _selectedPostprocessAfterPreviewImage = photo is null || string.IsNullOrWhiteSpace(afterPreviewPath) || !File.Exists(afterPreviewPath)
            ? null
            : CreatePreviewImage(afterPreviewPath);
        RaisePropertyChanged(nameof(SelectedPostprocessBeforePreviewImage));
        RaisePropertyChanged(nameof(SelectedPostprocessAfterPreviewImage));
        RaisePropertyChanged(nameof(SelectedPostprocessBeforePreviewDisplayWidth));
        RaisePropertyChanged(nameof(SelectedPostprocessBeforePreviewDisplayHeight));
        RaisePropertyChanged(nameof(SelectedPostprocessAfterPreviewDisplayWidth));
        RaisePropertyChanged(nameof(SelectedPostprocessAfterPreviewDisplayHeight));
    }

    private string ResolveAfterPreviewPath(PostprocessPhotoItem photo)
    {
        if (File.Exists(photo.OutputPath))
        {
            return photo.OutputPath;
        }

        if (_processedPostprocessFiles.Contains(photo.FilePath) && File.Exists(photo.FilePath))
        {
            return photo.FilePath;
        }

        return string.Empty;
    }

    public void UpdatePostprocessPreviewViewport(bool isBefore, double width, double height)
    {
        if (isBefore)
        {
            _selectedPostprocessBeforeViewportWidth = Math.Max(1, width);
            _selectedPostprocessBeforeViewportHeight = Math.Max(1, height);
            RaisePropertyChanged(nameof(SelectedPostprocessBeforePreviewDisplayWidth));
            RaisePropertyChanged(nameof(SelectedPostprocessBeforePreviewDisplayHeight));
            return;
        }

        _selectedPostprocessAfterViewportWidth = Math.Max(1, width);
        _selectedPostprocessAfterViewportHeight = Math.Max(1, height);
        RaisePropertyChanged(nameof(SelectedPostprocessAfterPreviewDisplayWidth));
        RaisePropertyChanged(nameof(SelectedPostprocessAfterPreviewDisplayHeight));
    }

    public void AdjustPostprocessPreviewZoom(int mouseWheelDelta)
    {
        var factor = mouseWheelDelta > 0 ? 1.12 : 1 / 1.12;
        SelectedPostprocessPreviewZoom = Math.Clamp(SelectedPostprocessPreviewZoom * factor, 0.25, 4.0);
    }

    private void RefreshPostprocessPhotoState(PostprocessPhotoItem photo)
    {
        var outputPath = GetPostprocessOutputPath(photo.FilePath);
        photo.OutputPath = outputPath;
        var hasProcessed = _processedPostprocessFiles.Contains(photo.FilePath) || File.Exists(outputPath);
        photo.HasProcessedOutput = hasProcessed;
        photo.Status = photo.IsEnabled
            ? (hasProcessed ? T("Готово", "Processed") : T("Готово", "Ready"))
            : T("Пропуск", "Skip");
    }

    public void AppendPostprocessLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        PostprocessLogText += line + Environment.NewLine;
    }

    private static bool IsDerivedPostprocessOutput(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return fileName.EndsWith(".pp", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPostprocessOutputPath(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        return Path.Combine(directory, $"{stem}.pp{extension}");
    }

    private Dictionary<string, string> GetModelSettingsSnapshot(string modelPath)
    {
        if (_postprocessSettingsCache.TryGetValue(modelPath, out var cached))
        {
            return new Dictionary<string, string>(cached, StringComparer.OrdinalIgnoreCase);
        }

        return PostprocessSettings.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }

    private void SelectAllPostprocessPhotos()
    {
        foreach (var photo in PostprocessFiles)
        {
            photo.IsSelected = true;
        }

        SetSelectedPostprocessPhotos(PostprocessFiles);
    }

    private void ClearPostprocessSelection()
    {
        foreach (var photo in PostprocessFiles)
        {
            photo.IsSelected = false;
        }

        SetSelectedPostprocessPhotos([]);
    }

    private void DeleteSelectedPostprocessFiles()
    {
        var targets = _selectedPostprocessPhotos.Count > 0
            ? _selectedPostprocessPhotos.ToList()
            : SelectedPostprocessPhoto is null ? [] : [SelectedPostprocessPhoto];
        if (targets.Count == 0)
        {
            return;
        }

        var deleted = false;
        foreach (var photo in targets)
        {
            photo.PropertyChanged -= OnPostprocessPhotoPropertyChanged;
            try
            {
                if (File.Exists(photo.FilePath))
                {
                    File.Delete(photo.FilePath);
                    deleted = true;
                }
            }
            catch (Exception ex)
            {
                PostprocessLogText += $"{T("Ошибка удаления", "Delete error")}: {photo.FileName} - {ex.Message}{Environment.NewLine}";
            }

            PostprocessFiles.Remove(photo);
        }

        _selectedPostprocessPhotos.Clear();
        SelectedPostprocessPhoto = PostprocessFiles.FirstOrDefault();
        RaisePropertyChanged(nameof(SelectedPostprocessPhotosCount));
        RefreshPostprocessCommands();

        if (deleted)
        {
            _ = LoadPostprocessFilesAsync(allowAutoProcess: false);
        }
    }

    public async Task ProcessSelectedPostprocessAsync()
    {
        AppendPostprocessLog(T("[PROCESS] Button pressed.", "[PROCESS] Button pressed."));
        if (_isProcessingPostprocess)
        {
            AppendPostprocessLog(T("[PROCESS] Aborted: already processing.", "[PROCESS] Aborted: already processing."));
            return;
        }

        var enabledItems = PostprocessFiles
            .Where(item => item.IsEnabled)
            .ToList();
        AppendPostprocessLog(T(
            $"[PROCESS] Requested. Selected files: {enabledItems.Count}, Source: {EffectivePostprocessSourceFolder}",
            $"[PROCESS] Requested. Selected files: {enabledItems.Count}, Source: {EffectivePostprocessSourceFolder}"));
        AppendPostprocessLog(T(
            $"[PROCESS] Files: {string.Join(", ", enabledItems.Select(item => item.FileName))}",
            $"[PROCESS] Files: {string.Join(", ", enabledItems.Select(item => item.FileName))}"));
        if (enabledItems.Count == 0)
        {
            PostprocessStatusText = T("Нет файлов для обработки", "No files to process");
            AppendPostprocessLog(T("[PROCESS] Aborted: no checked files.", "[PROCESS] Aborted: no checked files."));
            return;
        }

        if (SelectedPostprocessModel is null)
        {
            PostprocessStatusText = T("Модель не выбрана", "No model selected");
            AppendPostprocessLog(T("[PROCESS] Aborted: no model selected.", "[PROCESS] Aborted: no model selected."));
            return;
        }

        _isProcessingPostprocess = true;
        RefreshPostprocessCommands();
        try
        {
            DisposePostprocessFolderWatcher();
            var total = enabledItems.Count;
            var service = new PostprocessService();
            var outputFolder = EffectivePostprocessSourceFolder;
            Directory.CreateDirectory(outputFolder);
            var settings = GetModelSettingsSnapshot(SelectedPostprocessModel.FilePath);
            AppendPostprocessLog(T(
                $"[MODEL] {SelectedPostprocessModel.Name} | Profile: {SelectedPostprocessModel.Profile} | File: {SelectedPostprocessModel.FilePath}",
                $"[MODEL] {SelectedPostprocessModel.Name} | Profile: {SelectedPostprocessModel.Profile} | File: {SelectedPostprocessModel.FilePath}"));
            AppendPostprocessLog(T(
                $"[SETTINGS] {string.Join(", ", settings.Select(pair => $"{pair.Key}={pair.Value}"))}",
                $"[SETTINGS] {string.Join(", ", settings.Select(pair => $"{pair.Key}={pair.Value}"))}"));
            var progress = new Progress<PostprocessProgress>(progressItem =>
            {
                PostprocessProgressMaximum = Math.Max(1, total);
                PostprocessProgressValue = Math.Clamp(progressItem.Current, 0, total);
                PostprocessProgressText = T(
                    $"Обрабатывается {progressItem.Current} из {total}: {progressItem.CurrentFileName}",
                    $"Processing {progressItem.Current} of {total}: {progressItem.CurrentFileName}");
                RaisePropertyChanged(nameof(PostprocessProgressValue));
                RaisePropertyChanged(nameof(PostprocessProgressMaximum));
                RaisePropertyChanged(nameof(PostprocessProgressText));
            });

            PostprocessStatusText = T($"Запуск: {SelectedPostprocessModel.Name}", $"Starting: {SelectedPostprocessModel.Name}");
            AppendPostprocessLog(T(
                $"[START] {SelectedPostprocessModel.Name} on {total} file(s). GPU={UseGpuForModels && SelectedPostprocessModel.SupportsGpu}",
                $"[START] {SelectedPostprocessModel.Name} on {total} file(s). GPU={UseGpuForModels && SelectedPostprocessModel.SupportsGpu}"));
            var logs = await service.ProcessAsync(
                enabledItems,
                SelectedPostprocessModel,
                outputFolder,
                settings,
                UseGpuForModels && SelectedPostprocessModel.SupportsGpu,
                progress);

            PostprocessLogText += string.Join(Environment.NewLine, logs) + Environment.NewLine;
            AppendPostprocessLog(T("[PROCESS] Temporary outputs created.", "[PROCESS] Temporary outputs created."));

            foreach (var photo in enabledItems)
            {
                photo.OutputPath = GetPostprocessOutputPath(photo.FilePath);
                _processedPostprocessFiles.Add(photo.FilePath);
                RefreshPostprocessPhotoState(photo);
            }

            PostprocessStatusText = T("Обработка завершена", "Processing finished");
            AppendPostprocessLog(T("[PROCESS] Finished.", "[PROCESS] Finished."));
            await LoadPostprocessFilesAsync(allowAutoProcess: false);
        }
        catch (Exception ex)
        {
            AppendPostprocessLog($"{T("Ошибка", "Error")}: {ex.Message}");
            AppendPostprocessLog(ex.ToString());
            WpfMessageBox.Show(ex.Message, WindowTitle, WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
        finally
        {
            _isProcessingPostprocess = false;
            ResetPostprocessFolderWatcher();
            RefreshPostprocessCommands();
        }
    }

    public async Task TestSelectedPostprocessModelAsync()
    {
        AppendPostprocessLog(T("[TEST] Button pressed.", "[TEST] Button pressed."));
        if (_isProcessingPostprocess)
        {
            AppendPostprocessLog(T("[TEST] Aborted: already processing.", "[TEST] Aborted: already processing."));
            return;
        }

        if (SelectedPostprocessModel is null)
        {
            PostprocessStatusText = T("Модель не выбрана", "No model selected");
            AppendPostprocessLog(T("[TEST] Aborted: no model selected.", "[TEST] Aborted: no model selected."));
            return;
        }

        var sample = PostprocessFiles.FirstOrDefault(item => item.IsSelected) ?? PostprocessFiles.FirstOrDefault();
        if (sample is null)
        {
            PostprocessStatusText = T("Нет файлов для теста", "No files to test");
            AppendPostprocessLog(T("[TEST] Aborted: no source files.", "[TEST] Aborted: no source files."));
            return;
        }

        _isProcessingPostprocess = true;
        RefreshPostprocessCommands();
        try
        {
            DisposePostprocessFolderWatcher();
            var tempFolder = Path.Combine(Path.GetTempPath(), "CR2RawBatchStudio", "ModelTest");
            Directory.CreateDirectory(tempFolder);
            var settings = GetModelSettingsSnapshot(SelectedPostprocessModel.FilePath);
            AppendPostprocessLog(T(
                $"[TEST] Model: {SelectedPostprocessModel.Name} | File: {SelectedPostprocessModel.FilePath}",
                $"[TEST] Model: {SelectedPostprocessModel.Name} | File: {SelectedPostprocessModel.FilePath}"));
            AppendPostprocessLog(T(
                $"[TEST] Sample: {sample.FileName}",
                $"[TEST] Sample: {sample.FileName}"));

            var service = new PostprocessService();
            var logs = await service.ProcessAsync(
                [sample],
                SelectedPostprocessModel,
                tempFolder,
                settings,
                UseGpuForModels && SelectedPostprocessModel.SupportsGpu,
                null);

            foreach (var line in logs)
            {
                AppendPostprocessLog($"[TEST] {line}");
            }

            AppendPostprocessLog(T("[TEST] Success.", "[TEST] Success."));
            PostprocessStatusText = T("Тест модели успешен", "Model test successful");
        }
        catch (Exception ex)
        {
            AppendPostprocessLog(T("[TEST] Failed.", "[TEST] Failed."));
            AppendPostprocessLog(ex.ToString());
            PostprocessStatusText = T("Тест модели завершился ошибкой", "Model test failed");
            WpfMessageBox.Show(ex.Message, WindowTitle, WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
        finally
        {
            _isProcessingPostprocess = false;
            ResetPostprocessFolderWatcher();
            RefreshPostprocessCommands();
        }
    }

    public async Task OverwriteSelectedPostprocessAsync()
    {
        AppendPostprocessLog(T("[OVERWRITE] Button pressed.", "[OVERWRITE] Button pressed."));
        if (_isProcessingPostprocess)
        {
            return;
        }

        var targets = PostprocessFiles
            .Where(item => item.IsEnabled && File.Exists(item.OutputPath))
            .ToList();
        AppendPostprocessLog(T(
            $"[OVERWRITE] Requested. Candidates: {targets.Count}",
            $"[OVERWRITE] Requested. Candidates: {targets.Count}"));
        if (targets.Count == 0)
        {
            PostprocessStatusText = T("Нет файлов для перезаписи", "No files to overwrite");
            AppendPostprocessLog(T("[OVERWRITE] Aborted: no processed temp files.", "[OVERWRITE] Aborted: no processed temp files."));
            return;
        }

        _isProcessingPostprocess = true;
        RefreshPostprocessCommands();
        try
        {
            DisposePostprocessFolderWatcher();
            foreach (var photo in targets)
            {
                var processedPath = File.Exists(photo.OutputPath) ? photo.OutputPath : GetPostprocessOutputPath(photo.FilePath);
                if (!File.Exists(processedPath))
                {
                    AppendPostprocessLog(T($"[OVERWRITE] Missing temp file for {photo.FileName}", $"[OVERWRITE] Missing temp file for {photo.FileName}"));
                    continue;
                }

                File.Copy(processedPath, photo.FilePath, true);
                if (!string.Equals(processedPath, photo.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Delete(processedPath);
                    }
                    catch
                    {
                    }
                }

                _processedPostprocessFiles.Add(photo.FilePath);
                photo.OutputPath = photo.FilePath;
                photo.HasProcessedOutput = true;
                photo.Status = T("Готово", "Processed");
                AppendPostprocessLog(T($"[OVERWRITE] {photo.FileName} replaced in Out.", $"[OVERWRITE] {photo.FileName} replaced in Out."));
            }

            PostprocessStatusText = T("Файлы перезаписаны", "Files overwritten");
            AppendPostprocessLog(T("[OVERWRITE] Finished.", "[OVERWRITE] Finished."));
            await LoadPostprocessFilesAsync(allowAutoProcess: false);
        }
        catch (Exception ex)
        {
            AppendPostprocessLog($"{T("Ошибка", "Error")}: {ex.Message}");
            AppendPostprocessLog(ex.ToString());
            WpfMessageBox.Show(ex.Message, WindowTitle, WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
        finally
        {
            _isProcessingPostprocess = false;
            ResetPostprocessFolderWatcher();
            RefreshPostprocessCommands();
        }
    }

    private bool CanProcessPostprocess()
    {
        return !_isProcessingPostprocess;
    }

    private bool CanOverwritePostprocess()
    {
        return !_isProcessingPostprocess;
    }

    private void RefreshPostprocessCommands()
    {
        BrowsePostprocessSourceCommand?.RaiseCanExecuteChanged();
        LoadPostprocessFilesCommand?.RaiseCanExecuteChanged();
        RefreshPostprocessFilesCommand?.RaiseCanExecuteChanged();
        ReloadPostprocessModelsCommand?.RaiseCanExecuteChanged();
        TestSelectedPostprocessModelCommand?.RaiseCanExecuteChanged();
        ProcessSelectedPostprocessCommand?.RaiseCanExecuteChanged();
        OverwriteSelectedPostprocessCommand?.RaiseCanExecuteChanged();
        SelectAllPostprocessCommand?.RaiseCanExecuteChanged();
        ClearPostprocessSelectionCommand?.RaiseCanExecuteChanged();
        DeleteSelectedPostprocessFilesCommand?.RaiseCanExecuteChanged();
        RaisePropertyChanged(nameof(PostprocessSelectedCountText));
        RaisePropertyChanged(nameof(PostprocessModelDescriptionText));
        RaisePropertyChanged(nameof(PostprocessContextText));
        RaisePropertyChanged(nameof(SelectedPostprocessModelPathText));
        RaisePropertyChanged(nameof(EffectivePostprocessSourceFolder));
        RaisePropertyChanged(nameof(EffectivePostprocessOutputFolder));
        RaisePropertyChanged(nameof(SelectedPostprocessPhotosCount));
    }

    private void RaisePostprocessTextProperties()
    {
        RaisePropertyChanged(nameof(PostprocessTabText));
        RaisePropertyChanged(nameof(PostprocessPanelText));
        RaisePropertyChanged(nameof(PostprocessSourceFolderText));
        RaisePropertyChanged(nameof(PostprocessSourceFolderTooltip));
        RaisePropertyChanged(nameof(PostprocessOutputFolderText));
        RaisePropertyChanged(nameof(PostprocessOutputFolderTooltip));
        RaisePropertyChanged(nameof(PostprocessProcessText));
        RaisePropertyChanged(nameof(PostprocessOverwriteText));
        RaisePropertyChanged(nameof(PostprocessUseGpuText));
        RaisePropertyChanged(nameof(PostprocessAutoProcessText));
        RaisePropertyChanged(nameof(PostprocessSelectedCountText));
        RaisePropertyChanged(nameof(PostprocessModelText));
        RaisePropertyChanged(nameof(PostprocessModelDescriptionText));
        RaisePropertyChanged(nameof(PostprocessProgressLabelText));
        RaisePropertyChanged(nameof(PostprocessPreviewText));
        RaisePropertyChanged(nameof(PostprocessBeforePreviewText));
        RaisePropertyChanged(nameof(PostprocessSelectedFileText));
        RaisePropertyChanged(nameof(PostprocessEnabledColumnText));
        RaisePropertyChanged(nameof(PostprocessFileColumnText));
        RaisePropertyChanged(nameof(PostprocessStatusColumnText));
        RaisePropertyChanged(nameof(PostprocessPlanHintText));
        RaisePropertyChanged(nameof(PostprocessContextText));
        RaisePropertyChanged(nameof(SelectedPostprocessPreviewName));
        RaisePropertyChanged(nameof(SelectedPostprocessModelPathText));
        RaisePropertyChanged(nameof(PostprocessStatusText));
    }

    private void ResetPostprocessFolderWatcher()
    {
        DisposePostprocessFolderWatcher();
        var sourceFolder = EffectivePostprocessSourceFolder;
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            return;
        }

        _postprocessFolderWatcher = new FileSystemWatcher(sourceFolder)
        {
            Filter = "*.jpg",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _postprocessFolderWatcher.Created += OnPostprocessFolderChanged;
        _postprocessFolderWatcher.Deleted += OnPostprocessFolderChanged;
        _postprocessFolderWatcher.Renamed += OnPostprocessFolderChanged;
    }

    private void DisposePostprocessFolderWatcher()
    {
        if (_postprocessFolderWatcher is null)
        {
            return;
        }

        _postprocessFolderWatcher.EnableRaisingEvents = false;
        _postprocessFolderWatcher.Created -= OnPostprocessFolderChanged;
        _postprocessFolderWatcher.Deleted -= OnPostprocessFolderChanged;
        _postprocessFolderWatcher.Renamed -= OnPostprocessFolderChanged;
        _postprocessFolderWatcher.Dispose();
        _postprocessFolderWatcher = null;
    }

    private void OnPostprocessFolderChanged(object sender, FileSystemEventArgs e)
    {
        if (!Path.GetExtension(e.FullPath).Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(e.FullPath).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _postprocessFolderRefreshCts?.Cancel();
        _postprocessFolderRefreshCts?.Dispose();
        _postprocessFolderRefreshCts = new CancellationTokenSource();
        var token = _postprocessFolderRefreshCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                System.Windows.Application.Current?.Dispatcher.Invoke(() => _ = LoadPostprocessFilesAsync());
            }
            catch (TaskCanceledException)
            {
            }
        }, token);
    }

    private void InitializePostprocessCommands()
    {
        BrowsePostprocessSourceCommand = new RelayCommand(_ => BrowsePostprocessSource(), _ => !_isProcessingPostprocess);
        LoadPostprocessFilesCommand = new RelayCommand(async _ => await LoadPostprocessFilesAsync(), _ => !_isLoadingPostprocessFiles);
        RefreshPostprocessFilesCommand = new RelayCommand(async _ => await LoadPostprocessFilesAsync(), _ => !_isLoadingPostprocessFiles);
        ReloadPostprocessModelsCommand = new RelayCommand(_ => LoadPostprocessModels(), _ => !_isProcessingPostprocess);
        TestSelectedPostprocessModelCommand = new RelayCommand(async _ => await TestSelectedPostprocessModelAsync(), _ => !_isProcessingPostprocess);
        ProcessSelectedPostprocessCommand = new RelayCommand(async _ => await ProcessSelectedPostprocessAsync(), _ => CanProcessPostprocess());
        OverwriteSelectedPostprocessCommand = new RelayCommand(async _ => await OverwriteSelectedPostprocessAsync(), _ => CanOverwritePostprocess());
        SelectAllPostprocessCommand = new RelayCommand(_ => SelectAllPostprocessPhotos(), _ => PostprocessFiles.Count > 0);
        ClearPostprocessSelectionCommand = new RelayCommand(_ => ClearPostprocessSelection(), _ => PostprocessFiles.Any(item => item.IsSelected));
        DeleteSelectedPostprocessFilesCommand = new RelayCommand(_ => DeleteSelectedPostprocessFiles(), _ => _selectedPostprocessPhotos.Count > 0);
    }
}
