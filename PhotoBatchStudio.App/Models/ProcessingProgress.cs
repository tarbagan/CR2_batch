namespace PhotoBatchStudio.App.Models;

public sealed class ProcessingProgress
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string CurrentFileName { get; init; } = string.Empty;
}
