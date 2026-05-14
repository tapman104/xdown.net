Add a new XDown.Cli project to the solution.

1. Create the project:
dotnet new console -n XDown.Cli -f net8.0 -o src/XDown.Cli
dotnet sln add src/XDown.Cli/XDown.Cli.csproj
2. XDown.Cli.csproj requirements:

SelfContained, PublishAot, PublishSingleFile — same flags as XDown.App
ProjectReference to XDown.Core
No Avalonia, no third-party packages

1. Program.cs — manual arg parsing, no libraries:
Arguments:

--url (required)
--output (optional, derive from URL via UrlHelper.DeriveFilename if omitted)
--segments (optional int, default 4)
--hash (optional, e.g. sha256:abc123)
--temp-dir (optional)
--no-progress (flag)

Progress output:

Overwrite same line with \r
Format: [=====>    ] 45% | 2.1 MB/s | ETA 00:32
If --no-progress: silent until done

Exit codes:

0 = success
1 = download failed
2 = checksum mismatch
3 = bad arguments

Errors go to stderr. Final success line goes to stdout:
Done. Saved to <path> (<size> in <time>)
4. AOT rules from gemini.md apply — no reflection, no dynamic, ConfigureAwait(false) in Core calls.
5. Build and verify: dotnet build
