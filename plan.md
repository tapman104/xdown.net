# Cross-Platform Downloader — Full Project Plan
>
> **For AI assistants (Gemini, Claude, GPT, etc.)**  
> This document is a complete, self-contained specification. Feed it as context at the start of every coding session. Each section tells you *what* to build, *why*, and *exactly how*. Follow the architecture strictly — do not invent alternatives unless a section explicitly says you can.

---

## 0. Project Identity

| Field | Value |
|---|---|
| **App name** |  |
| **Type** | Desktop GUI downloader |
| **Target** | Windows x64, Linux x64, macOS arm64/x64 |
| **UI framework** | Avalonia 11.x (MVVM, compiled XAML) |
| **Runtime** | .NET 9, Native AOT |
| **Language** | C# 13 |
| **Output** | Single self-contained executable per platform |
| **v1 scope** | Single-file download with segmented multi-thread, live progress bar, speed + ETA, cancel |

---

## 1. Repository Structure

```
xdown/
├── src/
│   ├── XDown.App/                  # Avalonia UI project (entry point)
│   │   ├── XDown.App.csproj
│   │   ├── App.axaml
│   │   ├── App.axaml.cs
│   │   ├── Views/
│   │   │   └── MainWindow.axaml
│   │   │   └── MainWindow.axaml.cs
│   │   ├── ViewModels/
│   │   │   └── MainViewModel.cs
│   │   └── Assets/
│   │       └── xdown-icon.ico
│   └── XDown.Core/                 # Platform-agnostic download logic (no UI refs)
│       ├── XDown.Core.csproj
│       ├── DownloadService.cs
│       ├── DownloadJob.cs
│       ├── DownloadProgress.cs
│       └── Helpers/
│           └── UrlHelper.cs
├── xdown.sln
└── README.md
```

> **AI instruction:** Always maintain this separation. `XDown.Core` must have zero references to Avalonia or any UI namespace. `XDown.App` depends on `XDown.Core`, never the reverse.

---

## 2. Project Files

### 2.1 `XDown.Core.csproj`

```xml
<!-- Pure class library, no UI, AOT-compatible -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsTrimmable>true</IsTrimmable>       <!-- Must stay true for AOT -->
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  </PropertyGroup>
</Project>
```

### 2.2 `XDown.App.csproj`

```xml
<!-- Avalonia GUI app with Native AOT -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>  <!-- Required on Windows AOT -->
    <ApplicationManifest>app.manifest</ApplicationManifest>

    <!-- AOT flags -->
    <PublishAot>true</PublishAot>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <InvariantGlobalization>true</InvariantGlobalization>
    <TrimMode>full</TrimMode>
    <PublishTrimmed>true</PublishTrimmed>
    <OptimizationPreference>Speed</OptimizationPreference>    <!-- Speed over Size for UI apps -->
    <StripSymbols>true</StripSymbols>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.2.*" />
    <PackageReference Include="Avalonia.Desktop" Version="11.2.*" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.*" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.*" />
    <ProjectReference Include="..\XDown.Core\XDown.Core.csproj" />
  </ItemGroup>
</Project>
```

> **AI instruction:** Pin Avalonia to `11.2.*` not latest. CommunityToolkit.Mvvm 8.3+ uses source generators which are AOT-safe. Do NOT add any package that uses reflection-based serialization (e.g. Newtonsoft.Json) — use `System.Text.Json` with source generation if JSON is ever needed.

---

## 3. Core Layer — `XDown.Core`

### 3.1 `DownloadProgress.cs`

```
// This is a plain data record. It is passed from DownloadService
// to the UI via IProgress<DownloadProgress>. Keep it immutable.
// No methods, no logic — just data.
```

```csharp
namespace XDown.Core;

public sealed record DownloadProgress(
    long BytesReceived,      // total bytes written so far (all segments combined)
    long TotalBytes,         // -1 if unknown (server didn't send Content-Length)
    double SpeedBytesPerSec, // rolling average over last 1 second
    TimeSpan Elapsed,
    TimeSpan? ETA            // null if TotalBytes == -1
);
```

### 3.2 `DownloadJob.cs`

```
// Represents one download request from the user.
// Immutable after construction. DownloadService reads this, never modifies it.
```

```csharp
namespace XDown.Core;

public sealed record DownloadJob(
    string Url,
    string OutputPath,       // full path including filename
    int MaxSegments = 4      // how many parallel HTTP range requests to use
);
```

### 3.3 `Helpers/UrlHelper.cs`

```
// Utility: derive a safe filename from a URL.
// Used by the ViewModel to auto-fill the output filename field.
// AOT-safe: no regex, no reflection.
```

```csharp
namespace XDown.Core.Helpers;

public static class UrlHelper
{
    public static string DeriveFilename(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrWhiteSpace(name) ? "download.bin" : name;
        }
        catch
        {
            return "download.bin";
        }
    }
}
```

### 3.4 `DownloadService.cs`

```
// This is the heart of the app. All HTTP and file I/O lives here.
// The UI never touches HttpClient directly.
//
// Architecture:
//   1. Send HEAD request → get Content-Length + check Accept-Ranges
//   2. If server supports ranges AND size > threshold → segmented download
//   3. Else → single-stream download
//   4. Segmented: write each segment to a .partN temp file
//   5. Merge temp files into final output in order
//   6. Delete temp files
//   7. Report progress via IProgress<DownloadProgress> throughout
//
// Threading model:
//   - Called from UI thread via async/await (Task-based)
//   - Internally uses Task.WhenAll for parallel segments
//   - CancellationToken is wired through every async call
//   - Progress callbacks are marshalled back via IProgress<T>
//     (Avalonia's dispatcher is NOT touched here — that's the ViewModel's job)
```

```csharp
namespace XDown.Core;

public sealed class DownloadService
{
    // Minimum file size to bother with segmentation (2MB)
    private const long SegmentThreshold = 2 * 1024 * 1024;
    // Buffer for reading HTTP response body
    private const int ReadBufferSize = 81920; // 80KB

    private readonly HttpClient _http;

    public DownloadService()
    {
        // SocketsHttpHandler is fully AOT-safe and cross-platform
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true,
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("XDown/1.0");
    }

    public async Task DownloadAsync(
        DownloadJob job,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        // Step 1: probe the URL
        var (totalBytes, supportsRanges) = await ProbeAsync(job.Url, ct);

        bool useSegments = supportsRanges
            && totalBytes > SegmentThreshold
            && job.MaxSegments > 1;

        if (useSegments)
            await DownloadSegmentedAsync(job, totalBytes, progress, ct);
        else
            await DownloadSingleAsync(job, totalBytes, progress, ct);
    }

    // ---------------------------------------------------------------
    // PROBE
    // Send HEAD request. Return (contentLength, acceptsRanges).
    // contentLength = -1 if unknown.
    // ---------------------------------------------------------------
    private async Task<(long ContentLength, bool AcceptsRanges)> ProbeAsync(
        string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Head, url);
        using var resp = await _http.SendAsync(req, ct);

        long len = resp.Content.Headers.ContentLength ?? -1;
        bool ranges = resp.Headers.AcceptRanges.Contains("bytes");
        return (len, ranges);
    }

    // ---------------------------------------------------------------
    // SINGLE STREAM DOWNLOAD
    // Simple GET, write to file, report progress every ~200ms.
    // ---------------------------------------------------------------
    private async Task DownloadSingleAsync(
        DownloadJob job,
        long totalBytes,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using var resp = await _http.GetAsync(job.Url,
            HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(job.OutputPath,
            FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: ReadBufferSize, useAsync: true);

        var buffer = new byte[ReadBufferSize];
        long received = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lastReport = sw.Elapsed;
        long lastBytes = 0;

        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;

            // Report at most ~5x per second
            if ((sw.Elapsed - lastReport).TotalMilliseconds >= 200)
            {
                double speed = (received - lastBytes)
                    / (sw.Elapsed - lastReport).TotalSeconds;
                TimeSpan? eta = (totalBytes > 0 && speed > 0)
                    ? TimeSpan.FromSeconds((totalBytes - received) / speed)
                    : null;

                progress?.Report(new DownloadProgress(
                    received, totalBytes, speed, sw.Elapsed, eta));

                lastReport = sw.Elapsed;
                lastBytes = received;
            }
        }

        // Final report
        progress?.Report(new DownloadProgress(
            received, totalBytes, 0, sw.Elapsed, TimeSpan.Zero));
    }

    // ---------------------------------------------------------------
    // SEGMENTED DOWNLOAD
    // Splits file into N equal byte ranges, downloads in parallel,
    // merges .partN temp files into final output.
    // ---------------------------------------------------------------
    private async Task DownloadSegmentedAsync(
        DownloadJob job,
        long totalBytes,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        int segCount = job.MaxSegments;
        long segSize = totalBytes / segCount;

        // Build segment descriptors
        var segments = new (long Start, long End, string TempPath)[segCount];
        for (int i = 0; i < segCount; i++)
        {
            long start = i * segSize;
            long end = (i == segCount - 1) ? totalBytes - 1 : (start + segSize - 1);
            segments[i] = (start, end, $"{job.OutputPath}.part{i}");
        }

        // Shared progress state — multiple threads write to this
        long totalReceived = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lastReport = sw.Elapsed;
        long lastBytes = 0;
        var reportLock = new object();

        void ReportProgress(long delta)
        {
            long current = Interlocked.Add(ref totalReceived, delta);
            var now = sw.Elapsed;

            lock (reportLock)
            {
                if ((now - lastReport).TotalMilliseconds < 200) return;
                double speed = (current - lastBytes) / (now - lastReport).TotalSeconds;
                TimeSpan? eta = speed > 0
                    ? TimeSpan.FromSeconds((totalBytes - current) / speed)
                    : null;
                progress?.Report(new DownloadProgress(
                    current, totalBytes, speed, sw.Elapsed, eta));
                lastReport = now;
                lastBytes = current;
            }
        }

        // Download all segments in parallel
        await Task.WhenAll(segments.Select(seg =>
            DownloadSegmentAsync(job.Url, seg.Start, seg.End, seg.TempPath, ReportProgress, ct)));

        // Merge
        await using var final = new FileStream(job.OutputPath,
            FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: ReadBufferSize, useAsync: true);

        foreach (var seg in segments)
        {
            await using var part = new FileStream(seg.TempPath,
                FileMode.Open, FileAccess.Read, FileShare.None,
                bufferSize: ReadBufferSize, useAsync: true);
            await part.CopyToAsync(final, ct);
        }

        // Cleanup temp files
        foreach (var seg in segments)
            try { File.Delete(seg.TempPath); } catch { /* best-effort */ }

        // Final progress report
        progress?.Report(new DownloadProgress(
            totalBytes, totalBytes, 0, sw.Elapsed, TimeSpan.Zero));
    }

    private async Task DownloadSegmentAsync(
        string url, long start, long end, string tempPath,
        Action<long> reportProgress, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);

        using var resp = await _http.SendAsync(req,
            HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(tempPath,
            FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: ReadBufferSize, useAsync: true);

        var buffer = new byte[ReadBufferSize];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            reportProgress(read);
        }
    }
}
```

---

## 4. UI Layer — `XDown.App`

### 4.1 `MainViewModel.cs`

```
// ViewModel for the single main window.
// Uses CommunityToolkit.Mvvm source generators ([ObservableProperty], [RelayCommand]).
// These are AOT-safe because they generate code at compile time, not runtime.
//
// Responsibilities:
//   - Hold URL input, output path, progress value, status text
//   - Expose StartDownload command and Cancel command
//   - Bridge IProgress<DownloadProgress> → Avalonia UI thread via Dispatcher
//   - Call DownloadService — never call HttpClient directly
```

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using XDown.Core;
using XDown.Core.Helpers;

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
```

### 4.2 `Views/MainWindow.axaml`

```
// Minimal but clean UI.
// ProgressBar IsIndeterminate binds to ProgressPercent == -1.
// All bindings use compiled XAML (x:CompileBindings="True") — required for AOT.
// DataContext is set in code-behind, not via XAML ServiceLocator.
```

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:XDown.App.ViewModels"
        x:Class="XDown.App.Views.MainWindow"
        x:DataType="vm:MainViewModel"
        x:CompileBindings="True"
        Title="xdown" Width="520" Height="280"
        CanResize="False">

  <StackPanel Margin="24" Spacing="12">

    <!-- URL input -->
    <TextBlock Text="URL" Classes="label"/>
    <TextBox Text="{Binding Url}"
             Watermark="https://example.com/file.zip"
             IsEnabled="{Binding !IsDownloading}"/>

    <!-- Output path -->
    <TextBlock Text="Save as" Classes="label"/>
    <TextBox Text="{Binding OutputPath}"
             IsEnabled="{Binding !IsDownloading}"/>

    <!-- Progress bar -->
    <ProgressBar Minimum="0" Maximum="100"
                 Value="{Binding ProgressPercent}"
                 IsIndeterminate="{Binding ProgressPercent, 
                     Converter={x:Static NegativeConverter.Instance}}"/>

    <!-- Status row -->
    <Grid ColumnDefinitions="*,Auto,Auto">
      <TextBlock Text="{Binding StatusText}" Grid.Column="0"/>
      <TextBlock Text="{Binding SpeedText}" Grid.Column="1" Margin="8,0"/>
      <TextBlock Text="{Binding EtaText}"   Grid.Column="2"/>
    </Grid>

    <!-- Buttons -->
    <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
      <Button Content="Download"
              Command="{Binding StartCommand}"/>
      <Button Content="Cancel"
              Command="{Binding CancelCommand}"
              IsEnabled="{Binding IsDownloading}"/>
    </StackPanel>

  </StackPanel>
</Window>
```

> **AI instruction:** `x:CompileBindings="True"` and `x:DataType` are mandatory on every XAML file. Without them, Avalonia falls back to reflection-based binding which breaks under AOT.

---

## 5. AOT-Specific Rules (READ BEFORE WRITING ANY CODE)

These rules apply to every file in the project. Breaking any of them will cause a silent runtime crash on the published AOT binary even if debug builds work fine.

| Rule | Detail |
|---|---|
| No `Assembly.GetTypes()` | Reflection-based type discovery breaks under trim |
| No `Activator.CreateInstance` | Use `new T()` directly |
| No `dynamic` keyword | Removed under AOT |
| No `XmlSerializer` | Use `System.Text.Json` with `[JsonSerializable]` source gen |
| No `Newtonsoft.Json` | Not AOT-safe |
| CommunityToolkit.Mvvm only via source gen | `[ObservableProperty]`, `[RelayCommand]` — never `ObservableObject.SetProperty` with reflection |
| Avalonia bindings | Always `x:CompileBindings="True"` + `x:DataType` |
| `IProgress<T>` callbacks | Always marshal to `Dispatcher.UIThread.Post(...)` in ViewModel |
| `HttpClient` | Use `SocketsHttpHandler` — the default `HttpClientHandler` may pull in platform-specific code |

---

## 6. Build & Publish Commands

```bash
# Windows x64
dotnet publish src/XDown.App -r win-x64 -c Release

# Linux x64
dotnet publish src/XDown.App -r linux-x64 -c Release

# macOS Apple Silicon
dotnet publish src/XDown.App -r osx-arm64 -c Release

# macOS Intel
dotnet publish src/XDown.App -r osx-x64 -c Release
```

Output lands in: `src/XDown.App/bin/Release/net9.0/<rid>/publish/`  
Expected binary size: ~15–25 MB (Avalonia AOT is larger than console AOT)

---

## 7. Implementation Order (Follow This Exactly)

Build in this order. Do not jump ahead. Each step must compile and run before moving to the next.

```
Step 1 → Create solution + two projects (XDown.Core, XDown.App)
Step 2 → Implement DownloadProgress.cs, DownloadJob.cs, UrlHelper.cs in Core
Step 3 → Implement DownloadService.cs — test with a console harness first
Step 4 → Set up Avalonia App.axaml, App.axaml.cs boilerplate
Step 5 → Implement MainViewModel.cs
Step 6 → Implement MainWindow.axaml with all bindings
Step 7 → Wire DataContext in MainWindow.axaml.cs
Step 8 → Run in debug mode and verify download works end-to-end
Step 9 → dotnet publish for win-x64, fix any AOT trim warnings
Step 10 → Repeat publish for linux-x64 and osx-arm64
```

---

## 8. Known Gotchas

**`IsIndeterminate` binding** — Avalonia's `ProgressBar` doesn't auto-detect -1. Use a value converter or a separate `bool IsIndeterminate` property in ViewModel bound to `Value < 0`.

**macOS sandboxing** — On macOS, writing to `~/Downloads` requires an entitlements file if you ever distribute via App Store. For direct distribution, no special setup needed.

**Linux file dialogs** — If you add a Browse button later, use `Avalonia.Platform.Storage` (StorageProvider API), not `System.Windows.Forms.OpenFileDialog`.

**`HttpClient` disposal** — Do NOT create a new `HttpClient` per request. The single instance in `DownloadService` constructor is correct. Disposing and recreating `HttpClient` is a common mistake that causes socket exhaustion.

**Segment count vs server** — Some servers return 200 instead of 206 even when Range header is sent. Always check `resp.StatusCode == HttpStatusCode.PartialContent` before assuming range was honored, and fall back to single-stream if not.

---

## 9. v2 Backlog (Out of Scope for Now)

- Download queue (multiple concurrent jobs)
- Resume interrupted downloads (`.xdown` metadata sidecar file)
- Browser extension integration (Chrome/Firefox native messaging)
- System tray icon with active download indicator
- Settings: default download folder, max segments, speed limit
- Clipboard URL detection on window focus

---

*End of plan. Feed this entire document to your AI assistant at the start of each session.*
