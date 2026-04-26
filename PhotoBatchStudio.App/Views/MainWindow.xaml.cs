using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhotoBatchStudio.App.Models;
using PhotoBatchStudio.App.ViewModels;
using WpfMessageBox = System.Windows.MessageBox;

namespace PhotoBatchStudio.App.Views;

public partial class MainWindow : Window
{
    private bool _isPreviewPanning;
    private System.Windows.Point _previewPanStartPoint;
    private double _previewPanStartHorizontalOffset;
    private double _previewPanStartVerticalOffset;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    private void MainTabControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || sender is not System.Windows.Controls.TabControl tabControl)
        {
            return;
        }

        if (tabControl.SelectedIndex == 2)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    viewModel.EnsurePostprocessModelsLoaded(forceReload: false);
                }
                catch (Exception ex)
                {
                    viewModel.AppendPostprocessLog($"[UI] Tab 3 load failed: {ex}");
                    WpfMessageBox.Show(ex.ToString(), "CR2(RAW) Batch Studio", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }
    }

    private void SortListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        viewModel.SetSelectedSortPhotos(listBox.SelectedItems.Cast<SortPhotoItem>());
    }

    private void PostprocessListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        viewModel.SetSelectedPostprocessPhotos(listBox.SelectedItems.Cast<PostprocessPhotoItem>());
    }

    private void SortListBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Delete || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.DeleteSelectedSortPhotosCommand.CanExecute(null))
        {
            viewModel.DeleteSelectedSortPhotosCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void PostprocessListBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        return;
    }

    private async void PostprocessTestButton_Click(object sender, RoutedEventArgs e)
    {
        await RunPostprocessActionAsync(vm => vm.TestSelectedPostprocessModelAsync(), "TEST");
    }

    private async void PostprocessProcessButton_Click(object sender, RoutedEventArgs e)
    {
        await RunPostprocessActionAsync(vm => vm.ProcessSelectedPostprocessAsync(), "PROCESS");
    }

    private async void PostprocessOverwriteButton_Click(object sender, RoutedEventArgs e)
    {
        await RunPostprocessActionAsync(vm => vm.OverwriteSelectedPostprocessAsync(), "OVERWRITE");
    }

    private async Task RunPostprocessActionAsync(Func<MainWindowViewModel, Task> action, string actionName)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            viewModel.AppendPostprocessLog($"[UI] {actionName} click.");
            await action(viewModel);
        }
        catch (Exception ex)
        {
            viewModel.AppendPostprocessLog($"[UI] {actionName} failed: {ex}");
            WpfMessageBox.Show(ex.ToString(), "CR2(RAW) Batch Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PostprocessDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || sender is not System.Windows.Controls.DataGrid dataGrid)
        {
            return;
        }

        viewModel.SetSelectedPostprocessPhotos(dataGrid.SelectedItems.Cast<PostprocessPhotoItem>());
    }

    private void PostprocessDataGrid_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        return;
    }

    private void PreviewScrollViewer_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.UpdatePreviewViewport(
            Math.Max(1, e.NewSize.Width - 24),
            Math.Max(1, e.NewSize.Height - 24));
    }

    private void PreviewScrollViewer_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        _isPreviewPanning = true;
        _previewPanStartPoint = e.GetPosition(scrollViewer);
        _previewPanStartHorizontalOffset = scrollViewer.HorizontalOffset;
        _previewPanStartVerticalOffset = scrollViewer.VerticalOffset;
        scrollViewer.CaptureMouse();
        scrollViewer.Cursor = System.Windows.Input.Cursors.SizeAll;
        e.Handled = true;
    }

    private void PreviewScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.AdjustPreviewZoom(e.Delta);
        e.Handled = true;
    }

    private void PreviewScrollViewer_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPreviewPanning || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var currentPoint = e.GetPosition(scrollViewer);
        var deltaX = currentPoint.X - _previewPanStartPoint.X;
        var deltaY = currentPoint.Y - _previewPanStartPoint.Y;

        scrollViewer.ScrollToHorizontalOffset(_previewPanStartHorizontalOffset - deltaX);
        scrollViewer.ScrollToVerticalOffset(_previewPanStartVerticalOffset - deltaY);
    }

    private void PreviewScrollViewer_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndPreviewPanning(sender as ScrollViewer);
    }

    private void PreviewScrollViewer_OnPreviewMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            return;
        }

        EndPreviewPanning(sender as ScrollViewer);
    }

    private void EndPreviewPanning(ScrollViewer? scrollViewer)
    {
        _isPreviewPanning = false;
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ReleaseMouseCapture();
        scrollViewer.Cursor = System.Windows.Input.Cursors.Arrow;
    }

    private void PostprocessBeforeScrollViewer_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.UpdatePostprocessPreviewViewport(
            isBefore: true,
            Math.Max(1, e.NewSize.Width - 24),
            Math.Max(1, e.NewSize.Height - 24));
    }

    private void PostprocessAfterScrollViewer_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.UpdatePostprocessPreviewViewport(
            isBefore: false,
            Math.Max(1, e.NewSize.Width - 24),
            Math.Max(1, e.NewSize.Height - 24));
    }

    private void PostprocessPreviewScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.AdjustPostprocessPreviewZoom(e.Delta);
        e.Handled = true;
    }

    private void PostprocessPreviewScrollViewer_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        _isPreviewPanning = true;
        _previewPanStartPoint = e.GetPosition(scrollViewer);
        _previewPanStartHorizontalOffset = scrollViewer.HorizontalOffset;
        _previewPanStartVerticalOffset = scrollViewer.VerticalOffset;
        scrollViewer.CaptureMouse();
        scrollViewer.Cursor = System.Windows.Input.Cursors.SizeAll;
        e.Handled = true;
    }

    private void PostprocessPreviewScrollViewer_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPreviewPanning || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var currentPoint = e.GetPosition(scrollViewer);
        var deltaX = currentPoint.X - _previewPanStartPoint.X;
        var deltaY = currentPoint.Y - _previewPanStartPoint.Y;

        scrollViewer.ScrollToHorizontalOffset(_previewPanStartHorizontalOffset - deltaX);
        scrollViewer.ScrollToVerticalOffset(_previewPanStartVerticalOffset - deltaY);
    }

    private void PostprocessPreviewScrollViewer_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndPreviewPanning(sender as ScrollViewer);
    }

    private void PostprocessPreviewScrollViewer_OnPreviewMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            return;
        }

        EndPreviewPanning(sender as ScrollViewer);
    }
}
