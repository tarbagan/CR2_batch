using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoBatchStudio.App.Models;

namespace PhotoBatchStudio.App.Services;

public sealed class PhotoBatchService : IPhotoBatchService
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".rw2", ".orf", ".raf"
    };

    public async Task<IReadOnlyList<string>> ProcessAsync(
        IReadOnlyList<PhotoFileItem> files,
        ProcessingSettings settings,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var log = new List<string>();

        await Task.Run(() =>
        {
            ValidateSettings(settings);
            Directory.CreateDirectory(settings.OutputFolder);

            log.Add($"Queued files: {files.Count}");
            log.Add($"Output folder: {settings.OutputFolder}");

            if (settings.UseExternalAi && !string.IsNullOrWhiteSpace(settings.ExternalAiCommand))
            {
                log.Add("External AI integration is enabled.");
            }

            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[index];
                var sourcePath = file.FullPath;
                var extension = Path.GetExtension(sourcePath);
                progress?.Report(new ProcessingProgress
                {
                    Current = index + 1,
                    Total = files.Count,
                    CurrentFileName = Path.GetFileName(sourcePath)
                });

                if (RawExtensions.Contains(extension))
                {
                    ProcessRawFile(sourcePath, settings, log);
                    continue;
                }

                ProcessRasterFile(sourcePath, settings, log);
            }
        }, cancellationToken);

        return log;
    }

    private static void ValidateSettings(ProcessingSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OutputFolder))
        {
            throw new InvalidOperationException("Output folder is required.");
        }

        if (!string.IsNullOrWhiteSpace(settings.XmpPresetPath) && !File.Exists(settings.XmpPresetPath))
        {
            throw new FileNotFoundException("XMP preset file was not found.", settings.XmpPresetPath);
        }

        if (!string.IsNullOrWhiteSpace(settings.XmpPresetPath) &&
            !string.Equals(Path.GetExtension(settings.XmpPresetPath), ".xmp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Preset must be an Adobe .xmp file.");
        }
    }

    private static void ProcessRawFile(string sourcePath, ProcessingSettings settings, ICollection<string> log)
    {
        var sourceFile = new FileInfo(sourcePath);
        if (!sourceFile.Exists)
        {
            log.Add($"[ERR] {sourceFile.Name}: source file not found.");
            return;
        }

        if (settings.RenderRawWithPhotoshop)
        {
            var outputJpegPath = Path.Combine(settings.OutputFolder, $"{Path.GetFileNameWithoutExtension(sourceFile.Name)}.jpg");
            RenderRawToJpegWithPhotoshop(sourceFile.FullName, outputJpegPath, settings);
            log.Add($"[OK] {sourceFile.Name}: rendered by Adobe Photoshop to {Path.GetFileName(outputJpegPath)}.");
            return;
        }

        var outputRawPath = Path.Combine(settings.OutputFolder, sourceFile.Name);
        File.Copy(sourceFile.FullName, outputRawPath, overwrite: true);

        var message = $"[OK] {sourceFile.Name}: RAW copied to output.";
        if (!string.IsNullOrWhiteSpace(settings.XmpPresetPath))
        {
            var outputXmpPath = Path.ChangeExtension(outputRawPath, ".xmp");
            if (File.Exists(outputXmpPath) && !settings.OverwriteXmp)
            {
                message += $" XMP skipped because it already exists: {Path.GetFileName(outputXmpPath)}.";
            }
            else
            {
                File.Copy(settings.XmpPresetPath, outputXmpPath, overwrite: true);
                message += $" XMP applied: {Path.GetFileName(outputXmpPath)}.";
            }
        }

        log.Add(message);
    }

    private static void ProcessRasterFile(string sourcePath, ProcessingSettings settings, ICollection<string> log)
    {
        var sourceFile = new FileInfo(sourcePath);
        if (!sourceFile.Exists)
        {
            log.Add($"[ERR] {sourceFile.Name}: source file not found.");
            return;
        }

        var outputPath = Path.Combine(settings.OutputFolder, sourceFile.Name);
        var extension = sourceFile.Extension.ToLowerInvariant();

        if (settings.UseExternalAi && !string.IsNullOrWhiteSpace(settings.ExternalAiCommand))
        {
            RunExternalAi(settings.ExternalAiCommand, sourceFile.FullName, outputPath);
            log.Add($"[OK] {sourceFile.Name}: processed by external AI command.");
            return;
        }

        using var stream = File.OpenRead(sourceFile.FullName);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var metadata = frame.Metadata as BitmapMetadata;
        var outputMetadata = metadata is not null ? metadata.Clone() as BitmapMetadata : new BitmapMetadata("jpg");

        if (outputMetadata is not null && !string.IsNullOrWhiteSpace(settings.Description))
        {
            TryWriteDescription(outputMetadata, settings.Description);
        }

        BitmapEncoder encoder = CreateEncoder(extension, settings.JpegQuality);
        encoder.Frames.Add(BitmapFrame.Create(frame, frame.Thumbnail, outputMetadata, frame.ColorContexts));

        using var output = File.Create(outputPath);
        encoder.Save(output);
        log.Add($"[OK] {sourceFile.Name}: image exported to output.");
    }

    private static void RenderRawToJpegWithPhotoshop(string rawPath, string outputJpegPath, ProcessingSettings settings)
    {
        var jobDirectory = Path.Combine(Path.GetTempPath(), "PhotoBatchStudio", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(jobDirectory);

        try
        {
            var workingRawPath = Path.Combine(jobDirectory, Path.GetFileName(rawPath));
            File.Copy(rawPath, workingRawPath, overwrite: true);

            if (!string.IsNullOrWhiteSpace(settings.XmpPresetPath))
            {
                var workingXmpPath = Path.ChangeExtension(workingRawPath, ".xmp");
                File.Copy(settings.XmpPresetPath, workingXmpPath, overwrite: true);
            }

            var configPath = Path.Combine(jobDirectory, "job.json");
            var jsxPath = Path.Combine(jobDirectory, "process_raw.jsx");
            var vbsPath = Path.Combine(jobDirectory, "run_photoshop_job.vbs");

            WriteJobConfig(configPath, workingRawPath, outputJpegPath, settings.JpegQuality);
            File.WriteAllText(jsxPath, BuildPhotoshopJsx(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(vbsPath, BuildPhotoshopVbs(), Encoding.ASCII);

            RunPhotoshopAutomation(vbsPath, jsxPath, configPath);

            if (!File.Exists(outputJpegPath))
            {
                throw new InvalidOperationException("Photoshop finished without creating the JPG file.");
            }

            if (!string.IsNullOrWhiteSpace(settings.Description))
            {
                UpdateJpegDescriptionInPlace(outputJpegPath, settings.Description, settings.JpegQuality);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(jobDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static BitmapEncoder CreateEncoder(string extension, int jpegQuality)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder
            {
                QualityLevel = Math.Clamp(jpegQuality, 1, 100)
            },
            ".png" => new PngBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
    }

    private static void TryWriteDescription(BitmapMetadata metadata, string description)
    {
        try
        {
            metadata.Comment = description;
        }
        catch
        {
        }

        TrySetQuery(metadata, "/app1/ifd/{ushort=270}", description);
        TrySetQuery(metadata, "/xmp/dc:description", description);
        TrySetQuery(metadata, "/xmp/dc:title", description);
    }

    private static void TrySetQuery(BitmapMetadata metadata, string query, object value)
    {
        try
        {
            metadata.SetQuery(query, value);
        }
        catch
        {
        }
    }

    private static void UpdateJpegDescriptionInPlace(string jpegPath, string description, int jpegQuality)
    {
        var tempPath = Path.Combine(Path.GetDirectoryName(jpegPath) ?? string.Empty, $"{Path.GetFileNameWithoutExtension(jpegPath)}.metadata.tmp");

        using (var stream = File.OpenRead(jpegPath))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var metadata = frame.Metadata as BitmapMetadata;
            var outputMetadata = metadata is not null ? metadata.Clone() as BitmapMetadata : new BitmapMetadata("jpg");
            if (outputMetadata is not null)
            {
                TryWriteDescription(outputMetadata, description);
            }

            var encoder = new JpegBitmapEncoder
            {
                QualityLevel = Math.Clamp(jpegQuality, 1, 100)
            };
            encoder.Frames.Add(BitmapFrame.Create(frame, frame.Thumbnail, outputMetadata, frame.ColorContexts));

            using var output = File.Create(tempPath);
            encoder.Save(output);
        }

        File.Copy(tempPath, jpegPath, overwrite: true);
        File.Delete(tempPath);
    }

    private static void WriteJobConfig(string configPath, string rawPath, string outputJpegPath, int jpegQuality)
    {
        string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        var json = $$"""
        {
          "rawPath": "{{Escape(rawPath)}}",
          "outputPath": "{{Escape(outputJpegPath)}}",
          "jpegQuality": {{Math.Clamp(jpegQuality, 1, 12)}}
        }
        """;

        File.WriteAllText(configPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void RunPhotoshopAutomation(string vbsPath, string jsxPath, string configPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cscript.exe",
            Arguments = $"//nologo {Quote(vbsPath)} {Quote(jsxPath)} {Quote(configPath)}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Photoshop automation.");
        process.WaitForExit();

        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();

        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException(
                $"Photoshop automation failed. Ensure Adobe Photoshop is installed and Camera Raw is available. {details}".Trim());
        }
    }

    private static string BuildPhotoshopVbs()
    {
        return
            "Option Explicit\r\n" +
            "\r\n" +
            "If WScript.Arguments.Count < 2 Then\r\n" +
            "    WScript.Echo \"Expected JSX path and config path.\"\r\n" +
            "    WScript.Quit 2\r\n" +
            "End If\r\n" +
            "\r\n" +
            "Dim photoshopApp\r\n" +
            "Dim scriptPath\r\n" +
            "Dim configPath\r\n" +
            "Dim result\r\n" +
            "\r\n" +
            "scriptPath = WScript.Arguments(0)\r\n" +
            "configPath = WScript.Arguments(1)\r\n" +
            "configPath = Replace(configPath, \"\\\", \"/\")\r\n" +
            "\r\n" +
            "On Error Resume Next\r\n" +
            "Set photoshopApp = CreateObject(\"Photoshop.Application\")\r\n" +
            "If Err.Number <> 0 Then\r\n" +
            "    WScript.Echo \"Unable to create Photoshop COM object: \" & Err.Description\r\n" +
            "    WScript.Quit 10\r\n" +
            "End If\r\n" +
            "\r\n" +
            "Err.Clear\r\n" +
            "photoshopApp.DoJavaScript \"$.setenv('PhotoBatchStudioConfigPath', '\" & Replace(configPath, \"'\", \"\\\\'\") & \"')\"\r\n" +
            "result = photoshopApp.DoJavaScriptFile(scriptPath, Null, 1)\r\n" +
            "If Err.Number <> 0 Then\r\n" +
            "    WScript.Echo \"DoJavaScriptFile failed: \" & Err.Description\r\n" +
            "    WScript.Quit 11\r\n" +
            "End If\r\n" +
            "\r\n" +
            "If Len(result) > 0 Then\r\n" +
            "    WScript.Echo result\r\n" +
            "End If\r\n";
    }

    private static string BuildPhotoshopJsx()
    {
        return
            "#target photoshop\r\n" +
            "\r\n" +
            "function readTextFile(filePath) {\r\n" +
            "    var file = new File(filePath);\r\n" +
            "    if (!file.exists) {\r\n" +
            "        throw new Error(\"Config file not found: \" + filePath);\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    file.encoding = \"UTF8\";\r\n" +
            "    file.open(\"r\");\r\n" +
            "    var content = file.read();\r\n" +
            "    file.close();\r\n" +
            "    return content;\r\n" +
            "}\r\n" +
            "\r\n" +
            "function parseConfig(text) {\r\n" +
            "    return eval(\"(\" + text + \")\");\r\n" +
            "}\r\n" +
            "\r\n" +
            "function ensureParentFolder(fileObject) {\r\n" +
            "    var folder = fileObject.parent;\r\n" +
            "    if (folder && !folder.exists) {\r\n" +
            "        folder.create();\r\n" +
            "    }\r\n" +
            "}\r\n" +
            "\r\n" +
            "function main() {\r\n" +
            "    var configPath = $.getenv(\"PhotoBatchStudioConfigPath\");\r\n" +
            "    if (!configPath) {\r\n" +
            "        throw new Error(\"Config path was not provided.\");\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    var config = parseConfig(readTextFile(configPath));\r\n" +
            "    var rawFile = new File(config.rawPath);\r\n" +
            "    var outputFile = new File(config.outputPath);\r\n" +
            "\r\n" +
            "    if (!rawFile.exists) {\r\n" +
            "        throw new Error(\"RAW file not found: \" + config.rawPath);\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    ensureParentFolder(outputFile);\r\n" +
            "\r\n" +
            "    var originalDialogs = app.displayDialogs;\r\n" +
            "    app.displayDialogs = DialogModes.NO;\r\n" +
            "\r\n" +
            "    try {\r\n" +
            "        var documentRef = app.open(rawFile);\r\n" +
            "        if (documentRef.layers.length > 1) {\r\n" +
            "            documentRef.flatten();\r\n" +
            "        }\r\n" +
            "\r\n" +
            "        var saveOptions = new JPEGSaveOptions();\r\n" +
            "        saveOptions.quality = config.jpegQuality;\r\n" +
            "        saveOptions.embedColorProfile = true;\r\n" +
            "        saveOptions.formatOptions = FormatOptions.STANDARDBASELINE;\r\n" +
            "        saveOptions.matte = MatteType.NONE;\r\n" +
            "\r\n" +
            "        documentRef.saveAs(outputFile, saveOptions, true, Extension.LOWERCASE);\r\n" +
            "        documentRef.close(SaveOptions.DONOTSAVECHANGES);\r\n" +
            "        return \"OK\";\r\n" +
            "    } finally {\r\n" +
            "        app.displayDialogs = originalDialogs;\r\n" +
            "    }\r\n" +
            "}\r\n" +
            "\r\n" +
            "main();\r\n";
    }

    private static void RunExternalAi(string commandTemplate, string inputPath, string outputPath)
    {
        var command = commandTemplate
            .Replace("{input}", Quote(inputPath), StringComparison.Ordinal)
            .Replace("{output}", Quote(outputPath), StringComparison.Ordinal);

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start external AI command.");
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var details = process.StandardError.ReadToEnd();
            if (string.IsNullOrWhiteSpace(details))
            {
                details = process.StandardOutput.ReadToEnd();
            }

            throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, "External AI command failed: {0}", details.Trim()));
        }
    }

    private static string Quote(string path) => $"\"{path}\"";
}
