namespace PhotoBatchStudio.App.Models;

public sealed class ProjectState
{
    public string Language { get; set; } = "ru";
    public List<string> QueueFiles { get; set; } = [];
    public string? XmpPresetPath { get; set; }
    public string OutputFolder { get; set; } = string.Empty;
    public string SortFolder { get; set; } = string.Empty;
    public string FinalFolder { get; set; } = string.Empty;
    public string ModelsRootFolder { get; set; } = string.Empty;
    public string PostprocessSourceFolder { get; set; } = string.Empty;
    public string PostprocessOutputFolder { get; set; } = string.Empty;
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
    public string SortMode { get; set; } = "name_asc";
    public List<string> FinalSelection { get; set; } = [];
    public bool PostprocessAutoProcess { get; set; }
    public bool PostprocessUseGpu { get; set; }
    public string PostprocessSelectedModelPath { get; set; } = string.Empty;
    public List<PostprocessPlanState> PostprocessPlans { get; set; } = [];
    public List<string> PostprocessFiles { get; set; } = [];
    public List<string> PostprocessSelectedFiles { get; set; } = [];
    public List<string> PostprocessProcessedFiles { get; set; } = [];
    public Dictionary<string, Dictionary<string, string>> PostprocessModelSettings { get; set; } = [];
}
