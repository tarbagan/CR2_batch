namespace PhotoBatchStudio.App.Models;

public sealed class PostprocessPlanState
{
    public string FilePath { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string SelectedModelPath { get; set; } = string.Empty;
}
