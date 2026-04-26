using PhotoBatchStudio.App.Models;

namespace PhotoBatchStudio.App.Services;

public interface IPostprocessService
{
    IReadOnlyList<PostprocessModelDescriptor> DiscoverModels(string modelsFolder);

    Task<IReadOnlyList<string>> ProcessAsync(
        IReadOnlyList<PostprocessPhotoItem> files,
        PostprocessModelDescriptor model,
        string outputFolder,
        IReadOnlyDictionary<string, string> settings,
        bool useGpu,
        IProgress<PostprocessProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
