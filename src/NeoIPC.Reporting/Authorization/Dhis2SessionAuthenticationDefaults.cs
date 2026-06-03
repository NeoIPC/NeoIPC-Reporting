namespace NeoIPC.Reporting.Authorization;

/// <summary>Constants for the DHIS2 session authentication scheme.</summary>
public static class Dhis2SessionAuthenticationDefaults
{
    /// <summary>The scheme name used when registering and applying the handler.</summary>
    public const string AuthenticationScheme = "Dhis2Session";
}

/// <summary>
/// Custom claim types issued by <see cref="Dhis2SessionAuthenticationHandler"/>.
/// </summary>
/// <remarks>
/// We deliberately use custom claim types rather than overloading
/// <c>ClaimTypes.Role</c>, because DHIS2's own user-role concept is
/// distinct: roles bundle authorities, and authorities are what we gate
/// on. <see cref="Authority"/> carries one DHIS2 authority string per
/// claim; <see cref="UserGroup"/> carries the group id (stable across
/// renames); <see cref="UserGroupName"/> carries the human-readable
/// group name and is recorded for diagnostics only — policies should
/// match on group id, not name.
/// </remarks>
public static class Dhis2ClaimTypes
{
    public const string Authority = "dhis2:authority";
    public const string UserGroup = "dhis2:userGroup";
    public const string UserGroupName = "dhis2:userGroupName";
}
