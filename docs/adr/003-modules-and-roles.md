# ADR 003: Modules, roles and permissions

- Status: accepted (2026-11-23)

## Context
Today one person uses the tool as a project manager. Developer, reviewer, QA, release manager and team lead roles come later.

## Decision
- A **module** (`IModule`) registers its services, its view model -> view mappings (`IViewRegistry`, no reflection)
  and its navigation items. Projects are named `WorkspaceManager.Modules.<Name>` ("Module" alone is a reserved word in VB, CA1716).
- A **role** (`Role`) is a list of module ids plus a set of permission names. The shell shows only the modules of the active role
  and the navigation items whose permission the role grants.
- Every command declares the permission it needs (`PermissionNames`). The only role today (`pm`) grants `*`.
- Shared data for several users will live on GitHub (issues, projects, pull requests), not on a server of our own.

## Consequences
- A new role = a new module + a role definition, without changes to existing modules.
- Restricting a role later needs no change in feature code.
