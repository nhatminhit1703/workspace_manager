namespace WorkspaceManager.Core.Modules;

/// <summary>A role = the modules it shows and the permissions it has.</summary>
public sealed record Role(string Id, string DisplayName, IReadOnlyList<string> ModuleIds, IReadOnlySet<string> Permissions)
{
    public bool Grants(string permission) => Permissions.Contains(PermissionNames.All) || Permissions.Contains(permission);
}

/// <summary>Permission names. Every command declares the permission it needs, so roles can be limited later without touching features.</summary>
public static class PermissionNames
{
    public const string All = "*";
    public const string RepoRead = "repo.read";
    public const string RepoFetch = "repo.fetch";
    public const string RepoCheckout = "repo.checkout";
    public const string RepoPull = "repo.pull";
    public const string RepoPush = "repo.push";
    public const string RepoPushForce = "repo.push.force";
    public const string ReleaseTag = "release.tag";
}

public static class BuiltInRoles
{
    public const string PmModuleId = "pm";

    /// <summary>The only role for now: one user acting as PM, every permission granted.</summary>
    public static Role ProjectManager { get; } = new(
        "pm",
        "Project manager",
        [PmModuleId],
        new HashSet<string>(StringComparer.Ordinal) { PermissionNames.All });
}
