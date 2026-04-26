namespace PhotoBatchStudio.App.Models;

public sealed class ProcessingSettings
{
    public string? XmpPresetPath { get; set; }
    public string OutputFolder { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExternalAiCommand { get; set; } = string.Empty;
    public double BlurThreshold { get; set; } = 120.0;
    public int JpegQuality { get; set; } = 92;
    public bool Recursive { get; set; } = true;
    public bool OverwriteXmp { get; set; }
    public bool DetectBlur { get; set; } = true;
    public bool AutoFixBlur { get; set; } = true;
    public bool UseExternalAi { get; set; }
    public bool RenderRawWithPhotoshop { get; set; } = true;
}
