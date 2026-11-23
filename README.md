# Workspace Manager

A desktop Git client and multi-repo workspace tool for project managers and developers,
built with **Avalonia + .NET 10** (Windows first, macOS later).

Status: **planning**. See [docs/PLAN.md](docs/PLAN.md) for the roadmap (in Vietnamese).

## Goals

- Everyday Git client: commit graph, line-level staging, diff, blame, stash, merge / rebase / cherry-pick,
  interactive rebase, 3-way conflict resolution, undo.
- Many repos at once: status grid, bulk fetch / checkout / pull / branch, cross-repo search, ticket view.
- GitHub integration: pull requests, reviews, checks, issues and projects.
- AI helpers: commit messages, PR descriptions, pre-push review, workspace reports.
- Role-based modules: PM first, then developer, reviewer, QA, release manager.

## Configuration

Nothing specific to a company or a workspace is stored in this repository.
Repo lists, server URLs and API keys live in a local configuration file on each machine.

## License

[MIT](LICENSE)
