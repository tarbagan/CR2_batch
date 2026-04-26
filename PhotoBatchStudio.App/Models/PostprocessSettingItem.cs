using PhotoBatchStudio.App.ViewModels;

namespace PhotoBatchStudio.App.Models;

public sealed class PostprocessSettingItem : ObservableObject
{
    private string _value = string.Empty;

    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Kind { get; init; } = "text";
    public string Tooltip { get; init; } = string.Empty;
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public IReadOnlyList<string> Options { get; init; } = [];

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}
