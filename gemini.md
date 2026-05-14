# C# Language & Runtime Context

> **For AI assistants (Gemini, Claude, GPT, etc.)**  
> Feed this file alongside the project plan at the start of every session.  
> This is the language rulebook. When in doubt about a C# decision, this file wins.

---

## 0. Versions in Use

| Thing | Version |
| --- | --- |
| C# | 13 |
| .NET | 8 |
| Runtime mode | Native AOT (published), JIT (debug/dev) |
| Nullable | Enabled everywhere |
| Implicit usings | Enabled |
| Target OS | Windows, Linux, macOS |

---

## 1. Language Features — What to Use

### 1.1 Records

Use `record` for immutable data bags. Use `sealed record` when the type will never be subclassed (which is almost always).

```csharp
// CORRECT — immutable, value-equality, deconstruct for free
public sealed record DownloadJob(string Url, string OutputPath, int MaxSegments = 4);

// WRONG — mutable class for data that never changes
public class DownloadJob { public string Url { get; set; } = ""; }
```

### 1.2 Nullable Reference Types

Every file has `#nullable enable` via the project setting. Treat warnings as bugs.

```csharp
// CORRECT
public string? OptionalName { get; init; }       // nullable — explicitly marked
public string RequiredName { get; init; } = "";  // non-nullable — has default

// WRONG — suppressing without reason
public string Name { get; set; } = null!;        // only use null! when you are 100% sure
```

### 1.3 Pattern Matching

Prefer `switch` expressions and patterns over chains of `if/else`.

```csharp
// CORRECT
string FormatBytes(long b) => b switch
{
    < 1024             => $"{b} B",
    < 1024 * 1024      => $"{b / 1024.0:F1} KB",
    < 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024):F1} MB",
    _                  => $"{b / (1024.0 * 1024 * 1024):F1} GB"
};

// WRONG
string FormatBytes(long b) {
    if (b < 1024) return b + " B";
    else if (b < 1024 * 1024) ...
}
```

### 1.4 Primary Constructors (C# 12+)

Use for simple dependency injection. Do NOT use when you need to validate constructor args.

```csharp
// CORRECT — simple DI case
public sealed class MainViewModel(DownloadService service) : ObservableObject
{
    private readonly DownloadService _service = service;
}

// WRONG for primary constructors — validation needed, use explicit ctor
public sealed class DownloadJob(string url, string outputPath)
{
    // Can't throw here easily — use explicit constructor instead
}
```

### 1.5 Collection Expressions (C# 12+)

Use `[]` syntax for collection literals.

```csharp
// CORRECT
string[] platforms = ["win-x64", "linux-x64", "osx-arm64"];
List<int> ids = [1, 2, 3];

// WRONG (old style)
var platforms = new string[] { "win-x64", "linux-x64" };
```

### 1.6 `using` declarations

Use the `using var` declaration form (no braces) for short-lived scopes. Use the block form when the disposal boundary needs to be explicit.

```csharp
// CORRECT — declaration form, disposes at end of enclosing scope
using var client = new HttpClient();

// CORRECT — block form when you need explicit early disposal
using (var file = new FileStream(...))
{
    // file disposed here exactly
}
```

### 1.7 String Interpolation vs Concatenation

Always use interpolation. Never `+` for more than two strings.

```csharp
// CORRECT
string msg = $"Downloaded {bytes} of {total} bytes";

// WRONG
string msg = "Downloaded " + bytes + " of " + total + " bytes";
```

### 1.8 `var` vs Explicit Types

Use `var` when the right-hand side makes the type obvious. Use explicit type when it adds clarity.

```csharp
// CORRECT — type is obvious from right side
var handler = new SocketsHttpHandler();
var sw = Stopwatch.StartNew();

// CORRECT — explicit when not obvious
HttpResponseMessage response = await _http.SendAsync(req, ct);

// WRONG — var hides important type info
var x = GetResult();  // what type is this?
```

---

## 2. Async / Threading Rules

These rules are non-negotiable. AOT + Avalonia has no tolerance for threading mistakes.

### 2.1 Always async all the way down

Never block an async call with `.Result` or `.Wait()`. This deadlocks on UI threads.

```csharp
// CORRECT
var data = await FetchAsync(ct);

// WILL DEADLOCK ON UI THREAD
var data = FetchAsync(ct).Result;
```

### 2.2 Always pass CancellationToken

Every async method must accept and forward `CancellationToken ct`. No exceptions.

```csharp
// CORRECT
public async Task DownloadAsync(DownloadJob job, CancellationToken ct)
{
    using var resp = await _http.GetAsync(job.Url, ct);
    await file.WriteAsync(buffer, ct);
}

// WRONG — token is ignored
public async Task DownloadAsync(DownloadJob job)
{
    using var resp = await _http.GetAsync(job.Url);  // uncancellable
}
```

### 2.3 ConfigureAwait

In `Neat.Core` (library code): always `ConfigureAwait(false)` — library code must not capture the UI sync context.  
In `XDown.App` (ViewModel/UI code): do NOT use `ConfigureAwait(false)` — you need to return to the UI thread.

```csharp
// CORRECT in XDown.Core
var response = await _http.SendAsync(req, ct).ConfigureAwait(false);

// CORRECT in XDown.App ViewModel
await _service.DownloadAsync(job, progress, ct);  // no ConfigureAwait — stay on UI thread
```

### 2.4 `IProgress<T>` for cross-thread UI updates

Never touch Avalonia UI objects from a background thread. Use `IProgress<T>` in Core, marshal in ViewModel.

```csharp
// CORRECT — Core reports raw data, never touches UI
progress?.Report(new DownloadProgress(received, total, speed, elapsed, eta));

// CORRECT — ViewModel marshals to UI thread
var progress = new Progress<DownloadProgress>(p =>
    Dispatcher.UIThread.Post(() => ProgressPercent = ...));

// WRONG — Core touching Avalonia directly
Dispatcher.UIThread.Post(() => ...);  // Core must not reference Avalonia
```

### 2.5 Interlocked for shared counters

When multiple tasks write to the same counter (e.g. segmented download progress), use `Interlocked`.

```csharp
// CORRECT
long current = Interlocked.Add(ref _totalReceived, delta);

// WRONG — data race
_totalReceived += delta;
```

### 2.6 Sliding Window Progress

For throughput/speed calculation, use a moving average (e.g., 5-second sliding window) using a `Queue<(long bytes, DateTime time)>` instead of simple snapshots. This prevents erratic ETA and speed reporting.

---

## 3. AOT Compatibility Rules

If any of these rules are broken, the AOT-published binary will crash at runtime even if debug builds work perfectly. The debug JIT fills in reflection gaps silently; AOT does not.

### 3.1 No runtime reflection

```csharp
// BANNED
Type t = typeof(MyClass);
t.GetProperties();                    // trimmed away
Activator.CreateInstance(t);          // no reflection in AOT
Assembly.GetExecutingAssembly().GetTypes();
```

### 3.2 No dynamic

```csharp
// BANNED
dynamic obj = GetSomething();
obj.DoThing();  // not supported under AOT
```

### 3.3 JSON — source generation only

```csharp
// CORRECT — source generator, AOT-safe
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsContext : JsonSerializerContext { }

var settings = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);

// BANNED — uses reflection
var settings = JsonSerializer.Deserialize<AppSettings>(json);
```

### 3.4 No XmlSerializer

```csharp
// BANNED — XmlSerializer uses reflection
var xs = new XmlSerializer(typeof(MyClass));

// Use System.Text.Json with source gen instead (see 3.3)
```

### 3.5 Generics — avoid open generics with reflection

```csharp
// SAFE — closed generic, resolved at compile time
var list = new List<DownloadJob>();

// RISKY — only safe if T is constrained to known types
void Process<T>(T item) where T : IJob { ... }
```

### 3.6 CommunityToolkit.Mvvm — source gen only

```csharp
// CORRECT — source generator emits the property at compile time
[ObservableProperty]
private string _url = string.Empty;

[RelayCommand(CanExecute = nameof(CanStart))]
private async Task StartAsync() { ... }

// BANNED — reflection-based
SetProperty(ref _url, value);  // do not call directly in AOT context
```

### 3.7 Avalonia XAML — compiled bindings mandatory

Every XAML file must have both of these on the root element:

```xml
x:CompileBindings="True"
x:DataType="vm:YourViewModel"
```

Without these, Avalonia uses reflection-based binding which is trimmed under AOT.

---

## 4. Error Handling Patterns

### 4.1 Exception hierarchy

```csharp
// For user-facing errors (bad URL, server error, disk full)
// Catch specific exceptions, convert to user-readable StatusText
catch (HttpRequestException ex)
    => StatusText = $"Network error: {ex.Message}";

catch (IOException ex)
    => StatusText = $"File error: {ex.Message}";

catch (OperationCanceledException)
    => StatusText = "Cancelled";

// WRONG — swallowing all exceptions
catch (Exception) { }  // never do this
```

### 4.2 Retry policy (manual, no Polly — keep AOT clean)

```csharp
// CORRECT — simple manual retry for transient network errors
int attempt = 0;
while (true)
{
    try
    {
        await DownloadSegmentAsync(..., ct);
        break;
    }
    catch (HttpRequestException) when (attempt < 3)
    {
        attempt++;
        await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
    }
}
```

### 4.3 Dispose safety

```csharp
// CORRECT — always null-check before disposing
_cts?.Cancel();
_cts?.Dispose();
_cts = null;
```

---

## 5. File I/O Rules

### 5.1 Always use async file I/O

```csharp
// CORRECT
await using var file = new FileStream(path,
    FileMode.Create, FileAccess.Write, FileShare.None,
    bufferSize: 81920, useAsync: true);

await file.WriteAsync(buffer.AsMemory(0, read), ct);

// WRONG — blocking I/O on async path
file.Write(buffer, 0, read);
```

### 5.2 Buffer size

Use `81920` (80KB) as the standard buffer size for file and HTTP stream reads. This is what .NET's `Stream.CopyToAsync` uses internally and is well-tuned for throughput.

### 5.3 Temp files

Name temp files deterministically: `{outputFilename}.part{index}`. Use a `.xdown` sidecar JSON file to track metadata (`Url`, `TotalBytes`, segment states) for safe resumption.

### 5.4 Resume Logic

1. **Validate**: Before resuming, check the `.xdown` sidecar. If the `Url` or `TotalBytes` don't match the current job, discard and start fresh.
2. **Skip**: If a `.partN` file exists and matches the expected segment range, adjust the `Range` header and `FileMode.Append` to resume from the last byte.
3. **Cleanup**: Delete all `.partN` files and the `.xdown` sidecar only after successful file merge. Use `try-catch` for best-effort deletion.

---

## 6. Naming Conventions

| Thing | Convention | Example |
| --- | --- | --- |
| Class | PascalCase | `DownloadService` |
| Interface | `I` + PascalCase | `IDownloadJob` |
| Method | PascalCase | `DownloadAsync` |
| Async method | suffix `Async` | `ProbeAsync` |
| Private field | `_camelCase` | `_totalReceived` |
| Property | PascalCase | `BytesReceived` |
| Local variable | camelCase | `totalBytes` |
| Constant | PascalCase | `ReadBufferSize` |
| Namespace | PascalCase, matches folder | `XDown.Core.Helpers` |
| CommunityToolkit backing field | `_camelCase` | `[ObservableProperty] private string _url` |

---

## 7. Project Dependency Rules

```text
XDown.Core       →  no dependencies on UI (portable)
XDown.App        →  depends on XDown.Core + Avalonia + CommunityToolkit.Mvvm
XDown.Cli        →  depends on XDown.Core (Native AOT console)
XDown.Tests      →  depends on XDown.Core (xUnit, JIT only)
XDown.Benchmarks →  depends on XDown.Core (BenchmarkDotNet, JIT/Release)
```

If you find yourself writing `using Avalonia` in a `XDown.Core` file, stop — it's wrong. Move the code to `XDown.App`.

---

## 8. HttpClient Rules

```csharp
// CORRECT — one instance per service, reused for lifetime of app
public sealed class DownloadService
{
    private readonly HttpClient _http;

    public DownloadService()
    {
        var handler = new SocketsHttpHandler   // cross-platform, AOT-safe
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        _http = new HttpClient(handler);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("XDown/1.0");
    }
}

// WRONG — new HttpClient per request (socket exhaustion)
public async Task Download(string url)
{
    using var client = new HttpClient();  // never do this
}

// WRONG — default HttpClientHandler (not guaranteed AOT-safe cross-platform)
var handler = new HttpClientHandler();
```

---

## 9. What NOT to Add (Keep the Binary Clean)

| Package | Reason to avoid |
| --- | --- |
| `Newtonsoft.Json` | Reflection-based, not AOT-safe |
| `Polly` | Heavy dependency, manual retry is fine for v1 |
| `MediatR` | Reflection-based, overkill for this app |
| `AutoMapper` | Reflection-based |
| `Microsoft.Extensions.DependencyInjection` | Adds ~2MB, manual DI is sufficient |
| `log4net` / `NLog` | Heavy, use `Microsoft.Extensions.Logging` abstraction only if needed |
| `RestSharp` | Wraps HttpClient with extra reflection overhead |
| `Avalonia.ReactiveUI` | ReactiveUI adds complexity and AOT friction; CommunityToolkit is sufficient |

---

## 10. Testing & Benchmarking Rules

### 10.1 No Mocks for Network
Use real HTTP endpoints (e.g., httpbin.org, speed.cloudflare.com) for integration tests in `XDown.Tests`. This ensures the full network stack is exercised.

### 10.2 Benchmark Consistency
Run benchmarks in `Release` configuration. Use `SimpleJob(RunStrategy.ColdStart)` when benchmarking network-bound operations to avoid server-side rate limiting or caching effects.

---

*End of C# context. Feed alongside `downloader-plan.md` at the start of every session.*