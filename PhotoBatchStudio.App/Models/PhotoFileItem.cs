namespace PhotoBatchStudio.App.Models;

public sealed class PhotoFileItem : ViewModels.ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string DirectoryPath { get; init; } = string.Empty;
    public string FileType { get; init; } = string.Empty;

    private string _status = "Queued";

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}
