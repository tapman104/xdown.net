# XDown

XDown is a cross-platform desktop download manager built with Avalonia and .NET. It provides a simple UI for downloading files with progress reporting, cancellation, segmented transfers, and resume support for multi-part downloads.

## Features

- Cross-platform desktop UI with Avalonia
- Single-stream and segmented HTTP downloads
- Resume support for segmented downloads using `.partN` files and a `.xdown` sidecar file
- Live progress, speed, and ETA updates
- Cancellation support
- Native AOT-oriented project settings for the app

## Project structure

- `src/XDown.Core` — download logic, progress models, and helpers
- `src/XDown.App` — Avalonia UI and view models
- `xdown.sln` — solution file

## Requirements

- .NET 8 SDK

## Build

```bash
dotnet build xdown.sln
```

## Run

```bash
dotnet run --project src/XDown.App/XDown.App.csproj
```

## Test

```bash
dotnet test xdown.sln
```

## Current UI

The app currently exposes:

- A URL field
- A destination path field
- A download button
- A cancel button
- Progress, speed, and ETA display
