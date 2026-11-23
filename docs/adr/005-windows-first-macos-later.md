# ADR 005: Windows first, macOS later, cross-platform code from day one

- Status: accepted (2026-11-23)

## Decision
- Releases target Windows until Phase 7; macOS packaging, signing and notarization come in Phase 7.
- Libraries (`Core`, `Git`, `Data`, modules) target `net10.0`, never `net10.0-windows`.
- Operating system specific code sits behind `IPlatformService` and `ISecretStore` (`WorkspaceManager.Platform`),
  Windows-only classes carry `[SupportedOSPlatform("windows")]`.
- CI builds and tests on **Windows and Ubuntu** from the start, so Windows-only code or path assumptions fail early.

## Consequences
- Phase 7 adds a macOS implementation of the platform services, a `macos-latest` CI job and a `.dmg`, not a rewrite.
