# XDown

**XDown** is a high-performance, cross-platform file downloader written in C# and .NET 8. It is designed with **Native AOT** compatibility in mind, ensuring minimal binary size and zero-dependency distribution.

## Key Features

- **Multi-Segmented Downloads**: Parallel HTTP range requests for maximum throughput.
- **Pause & Resume**: Deterministic resumption of interrupted downloads using `.xdown` sidecar files.
- **Native AOT Compatible**: Built for high performance and low resource usage.
- **Cross-Platform**: Runs on Windows, Linux, and macOS.
- **Checksum Validation**: Automatic verification of downloads using MD5, SHA1, SHA256, or SHA512.

## Project Structure

- `src/XDown.Core`: The core engine containing the `DownloadService` and shared logic. No UI dependencies.
- `src/XDown.App`: A cross-platform GUI implementation using **Avalonia UI**.
- `src/XDown.Cli`: A high-performance command-line interface for terminal usage.
- `src/XDown.Tests`: Full integration and unit test suite (xUnit).
- `src/XDown.Benchmarks`: Performance benchmarks using **BenchmarkDotNet**.

## Getting Started

### Prerequisites

- .NET 8 SDK or later.

### Running the CLI

```bash
dotnet run --project src/XDown.Cli -- "https://example.com/file.zip" "C:\Downloads\file.zip"
```

### Running the GUI App

```bash
dotnet run --project src/XDown.App
```

### Running Tests

```bash
dotnet test
```

### Running Benchmarks

```bash
dotnet run -c Release --project src/XDown.Benchmarks
```

## Development Rules

This project follows strict architectural rules defined in `gemini.md`. All contributions must maintain Native AOT compatibility (no runtime reflection, no `dynamic`, source-generated JSON only).

## License

MIT
