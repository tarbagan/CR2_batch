namespace PhotoBatchStudio.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (Current?.MainWindow?.DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.AppendPostprocessLog($"[UI] Unhandled exception: {e.Exception}");
        }

        System.Windows.MessageBox.Show(
            e.Exception.ToString(),
            "CR2(RAW) Batch Studio",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
        e.Handled = true;
    }
}
