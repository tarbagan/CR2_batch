using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PhotoBatchStudio.App.Models;
using System.Windows.Media.Imaging;

namespace PhotoBatchStudio.App.Services;

public sealed class PostprocessService : IPostprocessService
{
    public IReadOnlyList<PostprocessModelDescriptor> DiscoverModels(string modelsFolder)
    {
        var models = new List<PostprocessModelDescriptor>();
        if (!Directory.Exists(modelsFolder))
        {
            return models;
        }

        var rootModels = Directory.EnumerateFiles(modelsFolder, "*.onnx", SearchOption.TopDirectoryOnly);
        foreach (var onnxPath in rootModels)
        {
            var descriptor = LoadDescriptor(onnxPath, ResolveManifestPath(onnxPath), BuildDisplayName(onnxPath, null));
            models.Add(descriptor);
        }

        foreach (var modelDirectory in Directory.EnumerateDirectories(modelsFolder, "*", SearchOption.TopDirectoryOnly))
        {
            var onnxFiles = Directory.EnumerateFiles(modelDirectory, "*.onnx", SearchOption.TopDirectoryOnly).ToList();
            if (onnxFiles.Count == 0)
            {
                continue;
            }

            var directoryName = Path.GetFileName(modelDirectory);
            var hasMultipleModels = onnxFiles.Count > 1;
            foreach (var onnxPath in onnxFiles)
            {
                var displayName = BuildDisplayName(onnxPath, hasMultipleModels ? directoryName : null);
                var descriptor = LoadDescriptor(onnxPath, ResolveManifestPath(onnxPath, modelDirectory), displayName);
                models.Add(descriptor);
            }
        }

        if (models.Count == 0)
        {
            models.Add(new PostprocessModelDescriptor
            {
                Name = "NAFNet Deblur",
                FilePath = Path.Combine(modelsFolder, "NAFNet", "NAFNet-REDS-width64_v1.onnx"),
                FolderPath = Path.Combine(modelsFolder, "NAFNet"),
                Profile = "nafnet",
                Description = "Drop a NAFNet ONNX model into the models folder to enable deblur processing.",
                SupportsGpu = true,
                Settings =
                [
                    new PostprocessModelSettingDescriptor { Key = "tile_size", Label = "Tile size", Kind = "number", Value = "0", Tooltip = "0 = full frame, larger values may reduce memory use." },
                    new PostprocessModelSettingDescriptor { Key = "strength", Label = "Strength", Kind = "number", Value = "1.0", Tooltip = "Deblur strength multiplier.", Minimum = 0.1, Maximum = 2.0 }
                ]
            });
        }

        return models
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ProcessAsync(
        IReadOnlyList<PostprocessPhotoItem> files,
        PostprocessModelDescriptor model,
        string outputFolder,
        IReadOnlyDictionary<string, string> settings,
        bool useGpu,
        IProgress<PostprocessProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var log = new List<string>();
        Directory.CreateDirectory(outputFolder);

        if (!File.Exists(model.FilePath))
        {
            throw new FileNotFoundException($"Model file not found: {model.FilePath}", model.FilePath);
        }

        await Task.Run(() =>
        {
            log.Add($"Model: {model.Name}");
            log.Add($"Output folder: {outputFolder}");
            log.Add(useGpu ? "GPU acceleration requested." : "CPU execution requested.");

            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = files[index];
                progress?.Report(new PostprocessProgress
                {
                    Current = index + 1,
                    Total = files.Count,
                    CurrentFileName = item.FileName,
                    CurrentModel = model.Name
                });

                var outputPath = Path.Combine(outputFolder, BuildOutputFileName(item.FileName));
                ProcessImageWithFallback(model.FilePath, item.FilePath, outputPath, settings, useGpu, log);
                System.Windows.Application.Current?.Dispatcher.Invoke(() => item.Status = "Processed");
                log.Add($"[OK] {item.FileName} -> {Path.GetFileName(outputPath)}");
            }
        }, cancellationToken);

        return log;
    }

    private static void ProcessImageWithFallback(
        string modelPath,
        string inputPath,
        string outputPath,
        IReadOnlyDictionary<string, string> settings,
        bool useGpu,
        List<string> log)
    {
        var tileSize = GetTileSize(settings);
        var tileOverlap = GetTileOverlap(settings, tileSize);
        var attempts = useGpu ? new[] { true, false } : new[] { false };
        var sizeAttempts = BuildTileSizeAttempts(tileSize);

        Exception? lastError = null;
        foreach (var tryGpu in attempts)
        {
            foreach (var currentTileSize in sizeAttempts)
            {
                try
                {
                    using var session = CreateSession(modelPath, tryGpu);
                    LogSessionMetadata(session, log, tryGpu ? "GPU" : "CPU");
                    log.Add($"[TRY] Provider={(tryGpu ? "GPU" : "CPU")}, Tile={currentTileSize}, Overlap={tileOverlap}");
                    ProcessImageTiled(session, inputPath, outputPath, settings, currentTileSize, tileOverlap);
                    if (!tryGpu || currentTileSize != tileSize)
                    {
                        log.Add($"[OK] Processed with {(tryGpu ? "GPU" : "CPU")} and tile {currentTileSize}.");
                    }

                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    log.Add($"[{(tryGpu ? "GPU" : "CPU")}] Tile {currentTileSize} failed: {ex.Message}");
                }
            }
        }

        throw lastError ?? new InvalidOperationException("Postprocess failed.");
    }

    private static InferenceSession CreateSession(string modelPath, bool useGpu)
    {
        var options = new SessionOptions();
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        if (useGpu)
        {
            try
            {
                options.AppendExecutionProvider_DML(0);
            }
            catch
            {
            }
        }

        return new InferenceSession(modelPath, options);
    }

    private static void ProcessImage(InferenceSession session, string inputPath, string outputPath, IReadOnlyDictionary<string, string> settings)
    {
        var inputBitmap = DecodeBitmap(inputPath);
        var tensorInfo = PrepareInputTensor(inputBitmap, 64);
        var inputName = session.InputMetadata.Keys.FirstOrDefault() ?? "input";

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, tensorInfo.tensor)
        };

        using var results = session.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();
        var outputBitmap = BuildBitmapFromTensor(outputTensor, tensorInfo.width, tensorInfo.height, tensorInfo.paddedWidth, tensorInfo.paddedHeight);
        SaveJpeg(outputBitmap, outputPath, GetJpegQuality(settings));
    }

    private static void ProcessImageTiled(
        InferenceSession session,
        string inputPath,
        string outputPath,
        IReadOnlyDictionary<string, string> settings,
        int tileSize,
        int tileOverlap)
    {
        var inputBitmap = DecodeBitmap(inputPath);
        var width = inputBitmap.PixelWidth;
        var height = inputBitmap.PixelHeight;
        var inputName = session.InputMetadata.Keys.FirstOrDefault() ?? "input";
        var outputPixels = new byte[width * height * 4];
        var accumB = new float[width * height];
        var accumG = new float[width * height];
        var accumR = new float[width * height];
        var weights = new float[width * height];
        var bytesPerPixel = 4;
        var stride = width * bytesPerPixel;

        tileSize = Math.Max(64, tileSize);
        tileOverlap = Math.Clamp(tileOverlap, 0, Math.Max(0, tileSize / 2));

        for (var tileY = 0; tileY < height; tileY += tileSize)
        {
            var tileHeight = Math.Min(tileSize, height - tileY);
            for (var tileX = 0; tileX < width; tileX += tileSize)
            {
                var tileWidth = Math.Min(tileSize, width - tileX);
                var srcX = Math.Max(0, tileX - tileOverlap);
                var srcY = Math.Max(0, tileY - tileOverlap);
                var srcRight = Math.Min(width, tileX + tileWidth + tileOverlap);
                var srcBottom = Math.Min(height, tileY + tileHeight + tileOverlap);
                var srcWidth = srcRight - srcX;
                var srcHeight = srcBottom - srcY;

                var cropped = new CroppedBitmap(inputBitmap, new System.Windows.Int32Rect(srcX, srcY, srcWidth, srcHeight));
                var tensorInfo = PrepareInputTensor(cropped, 64);
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(inputName, tensorInfo.tensor)
                };

                using var results = session.Run(inputs);
                var outputTensor = results.First().AsTensor<float>();
                var tileBitmap = BuildBitmapFromTensor(outputTensor, srcWidth, srcHeight, tensorInfo.paddedWidth, tensorInfo.paddedHeight);
                var tilePixels = new byte[srcWidth * srcHeight * bytesPerPixel];
                tileBitmap.CopyPixels(tilePixels, srcWidth * bytesPerPixel, 0);

                var hasLeftNeighbor = srcX > 0;
                var hasRightNeighbor = srcRight < width;
                var hasTopNeighbor = srcY > 0;
                var hasBottomNeighbor = srcBottom < height;

                for (var row = 0; row < srcHeight; row++)
                {
                    var globalY = srcY + row;
                    if (globalY < 0 || globalY >= height)
                    {
                        continue;
                    }

                    var yWeight = ComputeTileAxisWeight(row, srcHeight, hasTopNeighbor, hasBottomNeighbor, tileOverlap);
                    if (yWeight <= 0f)
                    {
                        continue;
                    }

                    for (var col = 0; col < srcWidth; col++)
                    {
                        var globalX = srcX + col;
                        if (globalX < 0 || globalX >= width)
                        {
                            continue;
                        }

                        var xWeight = ComputeTileAxisWeight(col, srcWidth, hasLeftNeighbor, hasRightNeighbor, tileOverlap);
                        var weight = xWeight * yWeight;
                        if (weight <= 0f)
                        {
                            continue;
                        }

                        var sourceIndex = (row * srcWidth + col) * bytesPerPixel;
                        var destIndex = globalY * width + globalX;
                        accumB[destIndex] += tilePixels[sourceIndex] * weight;
                        accumG[destIndex] += tilePixels[sourceIndex + 1] * weight;
                        accumR[destIndex] += tilePixels[sourceIndex + 2] * weight;
                        weights[destIndex] += weight;
                    }
                }
            }
        }

        for (var i = 0; i < width * height; i++)
        {
            var weight = weights[i];
            var b = weight > 0f ? accumB[i] / weight : 0f;
            var g = weight > 0f ? accumG[i] / weight : 0f;
            var r = weight > 0f ? accumR[i] / weight : 0f;
            outputPixels[i * 4] = ClampToByte(b / 255f);
            outputPixels[i * 4 + 1] = ClampToByte(g / 255f);
            outputPixels[i * 4 + 2] = ClampToByte(r / 255f);
            outputPixels[i * 4 + 3] = 255;
        }

        var source = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, outputPixels, stride);
        source.Freeze();
        SaveJpeg(source, outputPath, GetJpegQuality(settings));
    }

    private static float ComputeTileAxisWeight(int position, int length, bool hasLowerNeighbor, bool hasUpperNeighbor, int overlap)
    {
        if (overlap <= 0)
        {
            return 1f;
        }

        var weight = 1f;

        if (hasLowerNeighbor && position < overlap)
        {
            weight = Math.Min(weight, position / (float)overlap);
        }

        if (hasUpperNeighbor && position >= length - overlap)
        {
            weight = Math.Min(weight, (length - 1 - position) / (float)Math.Max(1, overlap));
        }

        return Math.Clamp(weight, 0f, 1f);
    }

    private static int GetTileSize(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue("tile_size", out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
            value > 0)
        {
            return value;
        }

        return 512;
    }

    private static int GetTileOverlap(IReadOnlyDictionary<string, string> settings, int tileSize)
    {
        if (settings.TryGetValue("tile_overlap", out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return Math.Clamp(value, 0, Math.Max(0, tileSize / 2));
        }

        return Math.Min(32, Math.Max(0, tileSize / 2));
    }

    private static int[] BuildTileSizeAttempts(int initialTileSize)
    {
        var candidates = new List<int>
        {
            Math.Max(64, initialTileSize),
            512,
            384,
            256,
            192,
            128
        };

        return candidates
            .Where(value => value > 0)
            .Distinct()
            .OrderByDescending(value => value)
            .ToArray();
    }

    private static void LogSessionMetadata(InferenceSession session, List<string> log, string providerLabel)
    {
        log.Add($"[{providerLabel}] Input count: {session.InputMetadata.Count}");
        foreach (var entry in session.InputMetadata)
        {
            var shape = entry.Value.Dimensions is null ? string.Empty : string.Join("x", entry.Value.Dimensions.Select(d => d.ToString(CultureInfo.InvariantCulture)));
            log.Add($"[{providerLabel}] Input {entry.Key}: {shape} {entry.Value.ElementType}");
        }
    }

    private static (Tensor<float> tensor, int width, int height, int paddedWidth, int paddedHeight) PrepareInputTensor(BitmapSource bitmap, int padMultiple)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var paddedWidth = AlignToMultiple(width, padMultiple);
        var paddedHeight = AlignToMultiple(height, padMultiple);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        var tensor = new DenseTensor<float>(new[] { 1, 3, paddedHeight, paddedWidth });
        for (var y = 0; y < paddedHeight; y++)
        {
            var srcY = Math.Min(y, height - 1);
            var row = srcY * stride;
            for (var x = 0; x < paddedWidth; x++)
            {
                var srcX = Math.Min(x, width - 1);
                var i = row + srcX * 4;
                tensor[0, 0, y, x] = pixels[i + 2] / 255f;
                tensor[0, 1, y, x] = pixels[i + 1] / 255f;
                tensor[0, 2, y, x] = pixels[i] / 255f;
            }
        }

        return (tensor, width, height, paddedWidth, paddedHeight);
    }

    private static BitmapSource BuildBitmapFromTensor(Tensor<float> tensor, int width, int height, int paddedWidth, int paddedHeight)
    {
        if (tensor.Dimensions.Length >= 4)
        {
            paddedWidth = tensor.Dimensions[^1] > 0 ? (int)tensor.Dimensions[^1] : paddedWidth;
            paddedHeight = tensor.Dimensions[^2] > 0 ? (int)tensor.Dimensions[^2] : paddedHeight;
        }

        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;
                var sourceY = Math.Min(y, paddedHeight - 1);
                var sourceX = Math.Min(x, paddedWidth - 1);
                var r = ClampToByte(tensor[0, 0, sourceY, sourceX]);
                var g = ClampToByte(tensor[0, 1, sourceY, sourceX]);
                var b = ClampToByte(tensor[0, 2, sourceY, sourceX]);
                pixels[index] = b;
                pixels[index + 1] = g;
                pixels[index + 2] = r;
                pixels[index + 3] = 255;
            }
        }

        var source = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    private static int AlignToMultiple(int value, int multiple)
    {
        if (multiple <= 1)
        {
            return value;
        }

        return ((value + multiple - 1) / multiple) * multiple;
    }

    private static byte ClampToByte(float value)
    {
        var scaled = value <= 1.0f ? value * 255f : value;
        return (byte)Math.Clamp((int)MathF.Round(scaled), 0, 255);
    }

    private static BitmapSource DecodeBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        return new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
    }

    private static void SaveJpeg(BitmapSource bitmap, string outputPath, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(outputPath);
        encoder.Save(output);
    }

    private static int GetJpegQuality(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue("jpeg_quality", out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return Math.Clamp(value, 1, 100);
        }

        return 92;
    }

    private static string BuildOutputFileName(string inputFileName)
    {
        var extension = Path.GetExtension(inputFileName);
        var stem = Path.GetFileNameWithoutExtension(inputFileName);
        return $"{stem}.pp{extension}";
    }

    private static PostprocessModelDescriptor LoadDescriptor(string onnxPath, string? manifestPath, string? displayName = null)
    {
        var folderPath = Path.GetDirectoryName(onnxPath) ?? string.Empty;
        if (manifestPath is null)
        {
            var folderName = Path.GetFileName(folderPath);
            var fileName = Path.GetFileNameWithoutExtension(onnxPath);
            var profile = fileName.Contains("nafnet", StringComparison.OrdinalIgnoreCase) || folderName.Contains("nafnet", StringComparison.OrdinalIgnoreCase)
                ? "nafnet"
                : "generic";
            return new PostprocessModelDescriptor
            {
                Name = displayName ?? (string.IsNullOrWhiteSpace(folderName) ? fileName : folderName),
                FilePath = onnxPath,
                FolderPath = folderPath,
                Profile = profile,
                Description = "No manifest found.",
                SupportsGpu = true,
                Settings = profile == "nafnet"
                    ? [
                        new PostprocessModelSettingDescriptor { Key = "tile_size", Label = "Tile size", Kind = "number", Value = "0" },
                        new PostprocessModelSettingDescriptor { Key = "strength", Label = "Strength", Kind = "number", Value = "1.0" }
                    ]
                    : []
            };
        }

        var json = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = json.RootElement;
        var settings = new List<PostprocessModelSettingDescriptor>();
        if (root.TryGetProperty("settings", out var settingsElement))
        {
            foreach (var element in settingsElement.EnumerateArray())
            {
                settings.Add(new PostprocessModelSettingDescriptor
                {
                    Key = element.GetProperty("key").GetString() ?? string.Empty,
                    Label = element.GetProperty("label").GetString() ?? string.Empty,
                    Kind = element.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "text" : "text",
                    Value = element.TryGetProperty("value", out var value) ? value.GetString() ?? string.Empty : string.Empty,
                    Tooltip = element.TryGetProperty("tooltip", out var tooltip) ? tooltip.GetString() ?? string.Empty : string.Empty
                });
            }
        }

        return new PostprocessModelDescriptor
        {
            Name = displayName ?? (root.TryGetProperty("name", out var name) ? name.GetString() ?? Path.GetFileNameWithoutExtension(onnxPath) : Path.GetFileNameWithoutExtension(onnxPath)),
            FilePath = onnxPath,
            FolderPath = folderPath,
            Profile = root.TryGetProperty("profile", out var profileEl) ? profileEl.GetString() ?? "generic" : "generic",
            Description = root.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty,
            SupportsGpu = !root.TryGetProperty("supportsGpu", out var gpuEl) || gpuEl.GetBoolean(),
            ManifestPath = manifestPath,
            Settings = settings
        };
    }

    private static string BuildDisplayName(string onnxPath, string? folderName)
    {
        var fileStem = Path.GetFileNameWithoutExtension(onnxPath);
        var cleanedStem = fileStem.Replace('_', ' ').Replace('-', ' ').Trim();
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return cleanedStem;
        }

        var cleanedFolder = folderName.Replace('_', ' ').Replace('-', ' ').Trim();
        return $"{cleanedFolder} - {cleanedStem}";
    }

    private static string? ResolveManifestPath(string onnxPath, string? directoryOverride = null)
    {
        var directory = directoryOverride ?? Path.GetDirectoryName(onnxPath);
        if (directory is null)
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(directory, "manifest.json"),
            Path.Combine(directory, "model.json"),
            Path.ChangeExtension(onnxPath, ".json")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
