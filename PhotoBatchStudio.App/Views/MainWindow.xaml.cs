using System.Windows;
using PhotoBatchStudio.App.ViewModels;

namespace PhotoBatchStudio.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
