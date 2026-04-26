namespace PhotoBatchStudio.App.Models;

public sealed class PostprocessProgress
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string CurrentFileName { get; init; } = string.Empty;
    public string CurrentModel { get; init; } = string.Empty;
}
