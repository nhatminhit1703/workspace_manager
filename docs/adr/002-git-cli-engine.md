# ADR 002: The git CLI is the Git engine

- Status: accepted (2026-11-23)

## Context
A Git client can use a library (libgit2 / LibGit2Sharp) or the installed `git` executable.

## Decision
- Every operation runs the user's `git` through `GitRunner`, with arguments in `ProcessStartInfo.ArgumentList`
  (no shell, no quoting bugs).
- Only machine-readable output is parsed: `status --porcelain=v2 -z`, `for-each-ref --format`, `log --format` with NUL separators.
- Fixed environment: `LC_ALL=C`, `GIT_TERMINAL_PROMPT=0`, `GCM_INTERACTIVE=never` (read operations never wait for a prompt),
  `GIT_OPTIONAL_LOCKS=0` (status does not take the index lock), `core.quotepath=false`.
- LibGit2Sharp may be added later for fast **reads** only, never for writes.

## Consequences
- Exactly git's behaviour: hooks, credential manager, LFS, worktrees, config.
- One process per command: reads run in parallel (25 repositories in about 3.6 s on the first real workspace).
- Parsers are covered by tests on recorded output and on throw-away repositories with an isolated git config.
