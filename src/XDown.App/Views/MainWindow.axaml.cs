using Avalonia.Controls;
using XDown.App.ViewModels;

namespace XDown.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
