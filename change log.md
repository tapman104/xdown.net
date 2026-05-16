Here's a clean changelog for XDown based on everything built this session:

---

## XDown Changelog

### v0.1.0 — Initial Release (2026-05-15)

**Core (`XDown.Core`)**
- Segmented parallel download with configurable segment count
- Resume support via `.xdown` sidecar file (JSON, per-segment byte offsets)
- Automatic retry with exponential backoff (up to 4 attempts on transient errors)
- SHA256/SHA1/SHA512/MD5 checksum verification post-download
- Rolling speed average and ETA calculation
- Pre-allocated output file (no merge step, direct segment write)
- `DownloadOptions` type separating behavior config from job identity
- `DownloadJob` simplified to URL + OutputPath only
- HTTP 206 enforcement on segmented requests (prevents silent file corruption)
- SSL validation enabled by default; bypass opt-in via constructor param
- Fixed speed calculation dividing by actual window seconds, not hardcoded 5

**CLI (`XDown.Cli`)**
- Positional URL support (`xdown https://...`)
- `--url`, `--output`, `--segments`, `--hash`, `--temp-dir`, `--no-progress` flags
- `--no-resume` flag to force fresh download
- `--help` / `-h` flag with full usage output
- `--version` / `-v` flag
- `\r` progress bar with percent, MB/s, and ETA
- Proper exit codes: 0 success, 1 error/cancelled, 2 checksum mismatch, 3 bad arguments
- Ctrl+C cancellation with clean exit

**UI (`XDown.App`)**
- Avalonia-based desktop app (win-x64, .NET 8)
- URL field with auto-derived output filename
- Output path auto-derivation respects user edits (won't overwrite manually set paths)
- Browse button using `StorageProvider.SaveFilePickerAsync` (AOT-safe)
- Progress bar with indeterminate mode when content-length unknown
- Speed and ETA display (`21.1 MB / 21.1 MB`, `2.3 MB/s`, `ETA 00:32`)
- "Open folder" action on download completion
- Advanced expander: Resume toggle, Segments numeric input (1–16)
- Download button disabled/labelled "Downloading…" while active
- Cancel button visible only during active download

**Tests & Benchmarks**
- xUnit test suite: URL helper, job validation, progress records, integration tests
- BenchmarkDotNet benchmarks: single vs segmented, 1/4/8 segment configurations

---

**Known issues (to fix in v0.2.0)**
- Window title not updating during download
- Segments NumericUpDown shows `4.0` instead of `4`
- "Open folder" placement inline with status row (should be separate row)