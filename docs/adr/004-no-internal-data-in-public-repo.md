# ADR 004: No company or workspace data in this public repository

- Status: accepted (2026-11-23)

## Context
The repository is public. An earlier version contained internal server URLs, repository lists and a company e-mail;
the history was rewritten and the repository recreated to remove them.

## Decision
- Never commit: internal hosts or URLs, repository lists, real ticket ids, e-mails, tokens, keys, personal paths.
- Workspace-specific values live in local files under the per-user app data folder
  (`%APPDATA%\WorkspaceManager` on Windows): `settings.json`, secrets encrypted with DPAPI (macOS Keychain later).
- Commits use the GitHub noreply e-mail.
- CI fails when a pattern from the `FORBIDDEN_PATTERNS` secret (kept outside the repository) appears in tracked files.

## Consequences
- Test data uses neutral names (`api`, `site`, `example.com`).
- Sharing a workspace configuration inside a team is done by exporting the local file, not through the repository.
