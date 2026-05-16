using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XDown.App.ViewModels;
using XDown.App.Services;
using System.Threading.Tasks;
using System.IO;

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
        
        // Load settings to get the last used folder
        var settingsService = new SettingsService();
        var settings = await settingsService.LoadAsync();
        IStorageFolder? suggestedStartLocation = null;
        
        if (!string.IsNullOrWhiteSpace(settings.LastFolder) && Directory.Exists(settings.LastFolder))
        {
            try
            {
                suggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(settings.LastFolder);
            }
            catch
            {
                // If we can't access the folder, just use default
            }
        }

        var saveFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save download as",
            SuggestedFileName = suggestedFileName,
            SuggestedStartLocation = suggestedStartLocation,
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
