using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XDown.App.ViewModels;

namespace XDown.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private async void OnBrowseOutputPathClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var suggestedFileName = vm.GetSuggestedFileName();
        var saveFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save download as",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = string.IsNullOrWhiteSpace(suggestedFileName)
                ? null
                : System.IO.Path.GetExtension(suggestedFileName)
        });

        if (saveFile is null)
            return;

        var selectedPath = saveFile.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        vm.SetOutputPathFromUser(selectedPath);
    }
}
