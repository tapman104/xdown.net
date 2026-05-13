using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using XDown.Core;
using XDown.Core.Helpers;
using System.IO;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace XDown.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DownloadService _service = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _url = string.Empty;

    [ObservableProperty] private string _outputPath = string.Empty;
    [ObservableProperty] private double _progressPercent;   // 0–100
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _speedText = string.Empty;
    [ObservableProperty] private string _etaText = string.Empty;
    [ObservableProperty] private bool _isDownloading;

    // Auto-fill filename when URL changes
    partial void OnUrlChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(OutputPath))
            OutputPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                UrlHelper.DeriveFilename(value));
    }

    private bool CanStart() => !IsDownloading && Uri.IsWellFormedUriString(Url, UriKind.Absolute);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        IsDownloading = true;
        ProgressPercent = 0;
        StatusText = "Connecting…";

        var progress = new Progress<DownloadProgress>(p =>
        {
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
            });
        });

        try
        {
            var job = new DownloadJob(Url, OutputPath);
            await _service.DownloadAsync(job, progress, _cts.Token);
            StatusText = "Done ✓";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            SpeedText = string.Empty;
            EtaText = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private static string FormatBytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024):F1} MB",
        _ => $"{b / (1024.0 * 1024 * 1024):F1} GB"
    };
}
