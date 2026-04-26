using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhotoBatchStudio.App.Models;
using PhotoBatchStudio.App.ViewModels;

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

    private void SortListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        viewModel.SetSelectedSortPhotos(listBox.SelectedItems.Cast<SortPhotoItem>());
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
}
