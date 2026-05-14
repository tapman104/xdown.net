```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6466/22H2/2022Update)
Intel Core i5-3470 CPU 3.20GHz (Ivy Bridge), 1 CPU, 4 logical and 4 physical cores
.NET SDK 8.0.417
  [Host]     : .NET 8.0.23 (8.0.23, 8.0.2325.60607), X64 RyuJIT x86-64-v2
  Job-AYUXIY : .NET 8.0.23 (8.0.23, 8.0.2325.60607), X64 RyuJIT x86-64-v2

IterationCount=3  RunStrategy=ColdStart  

```
| Method       | Segments | Mean    | Error   | StdDev   | Gen0      | Gen1      | Allocated |
|------------- |--------- |--------:|--------:|---------:|----------:|----------:|----------:|
| **Download10MB** | **1**        | **2.973 s** | **3.490 s** | **0.1913 s** |         **-** |         **-** | **492.37 KB** |
| **Download10MB** | **4**        | **2.893 s** | **1.123 s** | **0.0616 s** | **1000.0000** | **1000.0000** | **522.55 KB** |
| **Download10MB** | **8**        | **3.221 s** | **1.543 s** | **0.0846 s** | **1000.0000** | **1000.0000** | **701.16 KB** |
