Add XDown.Tests and XDown.Benchmarks projects to the solution.
Step 1 — XDown.Tests
dotnet new xunit -n XDown.Tests -f net8.0 -o src/XDown.Tests
dotnet sln add src/XDown.Tests/XDown.Tests.csproj
ProjectReference to XDown.Core only. No extra packages beyond the xunit template defaults.
Write these test files:
UrlHelperTests.cs — test DeriveFilename with: valid URL, URL with query string, trailing slash, garbage string, null/empty. All should return correct name or download.bin.
DownloadJobTests.cs — verify defaults: MaxSegments=4, TempDirectory=null, HashAlgorithm=null.
DownloadProgressTests.cs — record equality, ETA null when TotalBytes=-1, SpeedBytesPerSec=0 on final report.
DownloadServiceIntegrationTests.cs — real HTTP, no mocks:

Single-stream: <http://httpbin.org/bytes/1048576>
Segmented: <http://speed.cloudflare.com/__down?bytes=5242880>
Cancel mid-download: cts.Cancel() after 500ms, assert OperationCanceledException
Checksum pass: correct SHA256 on a known file
Checksum fail: wrong hash → assert DownloadException thrown + file deleted

Run dotnet test — all must pass before proceeding.

Step 2 — XDown.Benchmarks
dotnet new console -n XDown.Benchmarks -f net8.0 -o src/XDown.Benchmarks
dotnet sln add src/XDown.Benchmarks/XDown.Benchmarks.csproj
Add package: BenchmarkDotNet. ProjectReference to XDown.Core only.
Write DownloadBenchmarks.cs:

SingleStreamDownload — 10MB, 1 segment
SegmentedDownload — 10MB, [Params(1, 4, 8)] segments
Measure throughput MB/s across all param values

Run: dotnet run -c Release --project src/XDown.Benchmarks

AOT rules from gemini.md still apply to XDown.Core. Tests run under JIT — that's fine and expected. Do not write mocks — real HTTP only.
