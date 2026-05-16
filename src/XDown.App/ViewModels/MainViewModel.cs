using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using XDown.Core;
using XDown.Core.Helpers;
using XDown.App.Services;
using System.IO;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace XDown.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string AppTitle = "xdown";

    private readonly DownloadService _service = new();
    private readonly SettingsService _settingsService = new();
    private readonly LogService _logService = new();
    private CancellationTokenSource? _cts;
    private bool _outputPathUserEdited;
    private bool _isUpdatingOutputPathInternally;
    private string _lastAutoDerivedOutputPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _url = string.Empty;

    [ObservableProperty] private string _outputPath = string.Empty;
    [ObservableProperty] private double _progressPercent;   // 0–100
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _speedText = string.Empty;
    [ObservableProperty] private string _etaText = string.Empty;
    [ObservableProperty] private string _windowTitle = AppTitle;
    [ObservableProperty] private string _downloadButtonText = "Download";
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    private bool _canOpenFolder;

    [ObservableProperty] private bool _showOpenFolder;
    [ObservableProperty] private bool _resumeEnabled = true;
    [ObservableProperty] private int _segmentCount = 4;

    public MainViewModel()
    {
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        try
        {
            var settings = await _settingsService.LoadAsync();
            if (!string.IsNullOrWhiteSpace(settings.LastFolder) && Directory.Exists(settings.LastFolder))
            {
                // Use last saved folder with auto-derived filename
                var derivedFileName = UrlHelper.DeriveFilename(Url);
                var lastPath = Path.Combine(settings.LastFolder, derivedFileName);
                SetOutputPathInternal(lastPath);
                _lastAutoDerivedOutputPath = lastPath;
            }
        }
        catch
        {
            // If initialization fails, continue with defaults
        }
    }

    partial void OnSegmentCountChanged(int value)
    {
        if (value < 1)
            SegmentCount = 1;
        else if (value > 16)
            SegmentCount = 16;
    }

    // Auto-fill filename when URL changes
    partial void OnUrlChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            UpdateWindowTitle();
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPath) || !_outputPathUserEdited)
            SetOutputPathAutoDerived(value);

        UpdateWindowTitle();
    }

    partial void OnOutputPathChanged(string value)
    {
        if (_isUpdatingOutputPathInternally)
            return;

        _outputPathUserEdited = !string.Equals(value, _lastAutoDerivedOutputPath, StringComparison.OrdinalIgnoreCase);
        UpdateWindowTitle();
    }

    partial void OnIsDownloadingChanged(bool value)
    {
        DownloadButtonText = value ? "Downloading…" : "Download";
        UpdateWindowTitle();
    }

    private bool CanStart() => !IsDownloading && Uri.IsWellFormedUriString(Url, UriKind.Absolute);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        IsDownloading = true;
        ShowOpenFolder = false;
        CanOpenFolder = false;
        ProgressPercent = 0;
        StatusText = "Connecting…";

        var lastProgress = (long)0; // Track final progress for logging
        var lastTotalBytes = (long)0;
        var lastSpeedBytesPerSec = 0.0;

        var progress = new Progress<DownloadProgress>(p =>
        {
            lastProgress = p.BytesReceived;
            lastTotalBytes = p.TotalBytes;
            lastSpeedBytesPerSec = p.SpeedBytesPerSec;

            // IProgress<T> callbacks come from thread pool — marshal to UI thread
            Dispatcher.UIThread.Post(() =>
            {
                ProgressPercent = p.TotalBytes > 0
                    ? (double)p.BytesReceived / p.TotalBytes * 100
                    : -1; // indeterminate

                StatusText = p.TotalBytes > 0
                    ? $"{FormatBytes(p.BytesReceived)} / {FormatBytes(p.TotalBytes)}"
                    : FormatBytes(p.BytesReceived);

                SpeedText = $"{FormatBytes((long)p.SpeedBytesPerSec)}/s";
                EtaText = p.ETA.HasValue ? $"ETA {p.ETA:mm\\:ss}" : string.Empty;
                UpdateWindowTitle();
            });
        });

        var downloadSucceeded = false;

        try
        {
            var job = new DownloadJob(Url, OutputPath);
            var options = new DownloadOptions(MaxSegments: SegmentCount, Resume: ResumeEnabled);
            await _service.DownloadAsync(job, options, progress, _cts.Token);
            
            // Log successful download
            var fileName = GetSuggestedFileName();
            await _logService.LogDownloadSuccessAsync(fileName, Url, lastTotalBytes, lastSpeedBytesPerSec);
            
            StatusText = "Done";
            ShowOpenFolder = true;
            CanOpenFolder = Directory.Exists(GetOutputDirectory());
            downloadSucceeded = true;
            UpdateWindowTitle();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
            UpdateWindowTitle();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            UpdateWindowTitle();
            // Log download failure
            var fileName = GetSuggestedFileName();
            await _logService.LogDownloadFailureAsync(fileName, ex.Message);
        }
        finally
        {
            IsDownloading = false;
            SpeedText = string.Empty;
            EtaText = string.Empty;
            
            if (downloadSucceeded)
            {
                // Save last folder on successful download
                var outputDir = GetOutputDirectory();
                var settings = new AppSettings { LastFolder = outputDir };
                await _settingsService.SaveAsync(settings);
            }
            else
            {
                ShowOpenFolder = false;
                CanOpenFolder = false;
            }

            _cts?.Dispose();
            _cts = null;
            UpdateWindowTitle();
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        var outputDirectory = GetOutputDirectory();
        if (!Directory.Exists(outputDirectory))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = outputDirectory,
            UseShellExecute = true
        });
    }

    public void SetOutputPathFromUser(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        SetOutputPathInternal(path);
        _outputPathUserEdited = true;
    }

    public string GetSuggestedFileName()
    {
        var fromOutputPath = Path.GetFileName(OutputPath);
        if (!string.IsNullOrWhiteSpace(fromOutputPath))
            return fromOutputPath;

        return UrlHelper.DeriveFilename(Url);
    }

    private void SetOutputPathAutoDerived(string url)
    {
        var derivedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            UrlHelper.DeriveFilename(url));

        _lastAutoDerivedOutputPath = derivedPath;
        _outputPathUserEdited = false;
        SetOutputPathInternal(derivedPath);
    }

    private void SetOutputPathInternal(string path)
    {
        _isUpdatingOutputPathInternally = true;
        OutputPath = path;
        _isUpdatingOutputPathInternally = false;
    }

    private void UpdateWindowTitle()
    {
        var fileName = GetSuggestedFileName();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            WindowTitle = AppTitle;
            return;
        }

        if (IsDownloading)
        {
            var suffix = ProgressPercent >= 0
                ? $" ({Math.Round(ProgressPercent)}%)"
                : " (downloading…)";

            WindowTitle = $"{AppTitle} — {fileName}{suffix}";
            return;
        }

        if (ShowOpenFolder)
        {
            WindowTitle = $"{AppTitle} — {fileName} (Done)";
            return;
        }

        if (string.Equals(StatusText, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            WindowTitle = $"{AppTitle} — {fileName} (Cancelled)";
            return;
        }

        if (StatusText.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
        {
            WindowTitle = $"{AppTitle} — {fileName} (Error)";
            return;
        }

        WindowTitle = $"{AppTitle} — {fileName}";
    }

    private string GetOutputDirectory()
    {
        var outputDirectory = Path.GetDirectoryName(OutputPath);
        return string.IsNullOrWhiteSpace(outputDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : outputDirectory;
    }

    private static string FormatBytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024):F1} MB",
        _ => $"{b / (1024.0 * 1024 * 1024):F1} GB"
    };
}
