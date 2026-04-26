using PhotoBatchStudio.App.Models;

namespace PhotoBatchStudio.App.Services;

public interface IPhotoBatchService
{
    Task<IReadOnlyList<string>> ProcessAsync(
        IReadOnlyList<PhotoFileItem> files,
        ProcessingSettings settings,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
