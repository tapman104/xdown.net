Change 4 — Direct-Write, No Merge Step (touches only DownloadService.cs)
Instead of writing .partN files and merging:

Pre-allocate the final file to full size upfront (FileStream.SetLength(totalBytes))
Each segment opens the same file with FileShare.Write and seeks to its offset
Segments write directly in parallel — no merge phase at all
On resume (Change 3), check bytes already written at each offset instead of checking part files

Prerequisite: Do Change 3 first so the sidecar logic is already there

Change 5 — Optional Checksum (touches DownloadJob + DownloadService)
Step 5a — DownloadJob.cs
Add:
string? ExpectedHash     // e.g. "sha256:abc123..."
HashAlgorithmName HashAlgorithm  // default SHA256
Step 5b — DownloadService.cs

After merge/direct-write completes, if ExpectedHash != null:

Stream the final file through SHA256.HashData()
Compare hex strings
Throw DownloadException("Checksum mismatch") if wrong
Delete the corrupt file automatically

The Order Matters
1 → standalone, do anytime done
2 → standalone, do anytime done
3a → before 3b done
3b → before 4 (4 replaces the merge but reuses the sidecar) pending
4 → after 3pending
5 → after 4 pending
