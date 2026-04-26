namespace PhotoBatchStudio.App.Models;

public sealed class PostprocessModelDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public string Profile { get; init; } = "generic";
    public string Description { get; init; } = string.Empty;
    public bool SupportsGpu { get; init; } = true;
    public string? ManifestPath { get; init; }
    public List<PostprocessModelSettingDescriptor> Settings { get; init; } = [];
}

public sealed class PostprocessModelSettingDescriptor
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Kind { get; init; } = "text";
    public string Value { get; init; } = string.Empty;
    public string Tooltip { get; init; } = string.Empty;
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public List<string> Options { get; init; } = [];
}
