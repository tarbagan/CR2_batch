namespace PhotoBatchStudio.App.Models;

public sealed class PhotoFileItem
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string DirectoryPath { get; init; } = string.Empty;
    public string FileType { get; init; } = string.Empty;
    public string Status { get; set; } = "Queued";
}
