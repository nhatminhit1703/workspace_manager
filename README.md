# Workspace Manager

A desktop Git client and multi-repo workspace tool for project managers and developers,
built with **Avalonia + .NET 10** (Windows first, macOS later).

Status: **Phase 0 in progress** (foundation). See [docs/PLAN.md](docs/PLAN.md) for the roadmap (in Vietnamese)
and [docs/adr](docs/adr) for the architecture decisions.

## Goals

- Everyday Git client: commit graph, line-level staging, diff, blame, stash, merge / rebase / cherry-pick,
  interactive rebase, 3-way conflict resolution, undo.
- Many repos at once: status grid, bulk fetch / checkout / pull / branch, cross-repo search, issue view.
- GitHub integration: pull requests, reviews, checks, issues and projects.
- AI helpers: commit messages, PR descriptions, pre-push review, workspace reports.
- Role-based modules: PM first, then developer, reviewer, QA, release manager.

## Build and run

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) and `git` on the PATH.

```sh
dotnet build
dotnet test
dotnet run --project src/WorkspaceManager.App -- --workspace <folder that contains your repositories>
```

`--workspace` is only needed the first time; the app remembers it. Without it, the app asks for the folder.

## Layout

| Path | Content |
|---|---|
| `src/WorkspaceManager.Core` | Domain types and contracts (no UI, no OS code) |
| `src/WorkspaceManager.Git` | Git engine: runs the git CLI, parses porcelain output |
| `src/WorkspaceManager.Data` | Local settings |
| `src/WorkspaceManager.Platform` | OS services (Windows now, macOS later) |
| `src/Modules/WorkspaceManager.Modules.Pm` | PM role: repository grid (reports and dashboard next) |
| `src/WorkspaceManager.App` | Avalonia shell, composition root |
| `tests/` | xUnit v3 tests, including headless UI tests |

## Configuration

Nothing specific to a company or a workspace is stored in this repository (see [ADR 004](docs/adr/004-no-internal-data-in-public-repo.md)).
Settings and secrets live in `%APPDATA%\WorkspaceManager` on each machine.

## License

[MIT](LICENSE)
