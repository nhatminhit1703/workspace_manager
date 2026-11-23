# ADR 001: Avalonia UI on .NET 10 LTS

- Status: accepted (2026-11-23)

## Context
The tool started as PowerShell + WinForms. It has to become a full Git client (commit graph, diff, 3-way merge)
and must later run on macOS. WPF was chosen first, then dropped because it is Windows-only.

## Decision
- C# on **.NET 10 LTS** (supported until 11/2028; .NET 8 ends 11/2026, .NET 11 is STS).
- UI with **Avalonia 12** + CommunityToolkit.Mvvm (MVVM, compiled bindings).
- Microsoft.Extensions.* packages stay on the **10.x** line to match the runtime.

## Consequences
- Same XAML / MVVM model as WPF, runs on Windows, macOS and Linux.
- Fewer third-party controls than WPF: risky controls (commit graph, diff editor) are prototyped early.
- Test packages follow Avalonia: `xunit.v3` stays on 3.x while `Avalonia.Headless.XUnit` is built against 3.x.
- FluentAssertions 8 is not free for commercial use: **AwesomeAssertions** (MIT fork) is used instead.
