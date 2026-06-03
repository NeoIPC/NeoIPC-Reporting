namespace NeoIPC.Reporting;

/// <summary>
/// Service-wide configuration. Bound from the <c>"Reporting"</c>
/// configuration section (<c>appsettings.json</c> + standard
/// ASP.NET Core overrides).
/// </summary>
public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    /// <summary>
    /// Read-only directory holding the Surveillance-Toolkit reports tree
    /// (one subdirectory per report, plus shared resource dirs and
    /// top-level YAML files). In container builds this is <c>/reports</c>;
    /// in workspace builds the project's csproj <c>NeoIpcReportsDir</c>
    /// defaults it to the sibling Surveillance-Toolkit checkout.
    /// </summary>
    /// <remarks>
    /// The toolkit's <c>glossary*.yaml</c> files live one level above this
    /// directory (i.e. at <c>&lt;ReportsSourceDir&gt;/../glossary*.yaml</c>),
    /// matching the toolkit's repo layout. The per-render layout in
    /// <see cref="QuartoReportGenerator"/> picks them up from there.
    /// </remarks>
    public string ReportsSourceDir { get; set; } = "/reports";

    /// <summary>
    /// Per-render scratch root. Each render creates a fresh
    /// <c>render_&lt;random&gt;/</c> subdirectory under here. The
    /// per-render dir contains a symlink-tree layout (see
    /// <see cref="QuartoReportGenerator"/>) that mirrors the toolkit's
    /// repo structure so the QMD's relative reaches
    /// (<c>../common.yaml</c>, <c>../../glossary.yaml</c>, etc.) resolve.
    /// </summary>
    public string ReportsTempDir { get; set; } =
        Path.Combine(Path.GetTempPath(), "NeoIPC.Reporting");

    /// <summary>
    /// In-cluster DHIS2 base URL. Drives both the .NET-side admin-auth
    /// call to <c>/api/me</c> and (eventually) the R-side surveillance
    /// data fetch — single source of truth so an attacker can't redirect
    /// session-bearing traffic by flipping just one of the two.
    /// </summary>
    /// <remarks>
    /// Validated at startup by <see cref="Dhis2Endpoint"/> (rejects
    /// non-http/s schemes, userinfo, and loopback / unspecified
    /// addresses). The default matches the Compose service name; a
    /// non-default value should only ever come from a trusted deployment
    /// configuration.
    /// </remarks>
    public string Dhis2BaseUrl { get; set; } = "http://dhis2:8080";

    /// <summary>
    /// Selects the source-acquisition mode at build time. Used at
    /// runtime only to decide whether to set <c>NEOIPCR_DEV_PATH</c> on
    /// child R processes (in <see cref="BuildMode.Workspace"/> mode the
    /// service runs against the workspace's editable neoipcr checkout
    /// at <c>/neoipcr</c>; otherwise neoipcr is installed via pak and
    /// the env-var override is left unset).
    /// </summary>
    public BuildMode BuildMode { get; set; } = BuildMode.GithubMain;

    /// <summary>Storage root for admin-uploaded reference datasets.</summary>
    public string ReferenceDataDir { get; set; } = "/home/app/NeoIPC/ReferenceData";

    /// <summary>Storage root for admin-uploaded validation-exception files.</summary>
    public string ValidationExceptionsDir { get; set; } = "/home/app/NeoIPC/ValidationExceptions";
}

/// <summary>
/// How the Surveillance-Toolkit reports source was acquired in the build
/// of this image. Reflected at runtime to decide whether neoipcr is
/// expected at the well-known dev path.
/// </summary>
public enum BuildMode
{
    /// <summary>Default for nightly / dev images: cloned from GitHub <c>main</c>.</summary>
    GithubMain,

    /// <summary>Tagged release: cloned from a specific GitHub tag.</summary>
    GithubRelease,

    /// <summary>Workspace build: COPYed from the parent submodule checkout (editable).</summary>
    Workspace,
}
