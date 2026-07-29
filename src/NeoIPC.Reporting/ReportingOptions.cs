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
    /// top-level YAML files). In container builds this defaults to
    /// <c>/toolkit/reports</c>; in workspace IDE launches a relative
    /// path is resolved against the host environment's ContentRoot.
    /// </summary>
    /// <remarks>
    /// The toolkit's <c>glossary*.yaml</c> files live one level above this
    /// directory (i.e. at <c>&lt;ReportsSourceDir&gt;/../glossary*.yaml</c>),
    /// matching the toolkit's repo layout. The per-render layout in
    /// <see cref="QuartoReportProducer"/> picks them up from there.
    /// </remarks>
    public string ReportsSourceDir { get; set; } = "/toolkit/reports";

    /// <summary>
    /// Per-render scratch root. Each render creates a fresh
    /// <c>render_&lt;random&gt;/</c> subdirectory under here. The
    /// per-render dir contains a symlink-tree layout (see
    /// <see cref="QuartoReportProducer"/>) that mirrors the toolkit's
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
    public string Dhis2BaseUrl { get; set; } = "http://dhis2-backend:8080";

    /// <summary>
    /// Selects the source-acquisition mode at build time. Used at
    /// runtime only to decide whether to set <c>NEOIPCR_DEV_PATH</c> on
    /// child R processes (in <see cref="BuildMode.Workspace"/> mode the
    /// service runs against an editable neoipcr checkout at
    /// <see cref="NeoIpcrDevPath"/>; otherwise neoipcr is installed via
    /// pak and the env-var override is left unset).
    /// </summary>
    public BuildMode BuildMode { get; set; } = BuildMode.GithubBranch;

    /// <summary>
    /// Path passed as <c>NEOIPCR_DEV_PATH</c> to child R processes when
    /// <see cref="BuildMode"/> is <see cref="BuildMode.Workspace"/>.
    /// Container default is <c>/neoipcr</c>; workspace IDE launches
    /// override this with a path resolvable from the host ContentRoot
    /// to the workspace's neoipcr checkout.
    /// </summary>
    public string NeoIpcrDevPath { get; set; } = "/neoipcr";

    /// <summary>Storage root for admin-uploaded reference datasets.</summary>
    public string ReferenceDataDir { get; set; } = "/home/app/NeoIPC/ReferenceData";

    /// <summary>Storage root for admin-uploaded validation-exception files.</summary>
    public string ValidationExceptionsDir { get; set; } = "/home/app/NeoIPC/ValidationExceptions";

    /// <summary>
    /// Language subtags whose report localization is complete enough to
    /// offer and render. The warmup service registers a report's base
    /// English master and any <c>&lt;Report&gt;.&lt;lang&gt;.qmd</c>
    /// translation only when the language appears here — so a committed but
    /// incompletely-localized translation (missing its
    /// <c>_quarto-&lt;lang&gt;.yml</c>, sparse content) is neither advertised
    /// in the app's language picker nor resolved to via <c>Accept-Language</c>,
    /// both of which would otherwise fail the render. Defaults to English
    /// only; add a language once its translation is render-ready. Keep
    /// <c>"en"</c> (the source) listed unless deliberately disabling the
    /// report entirely — dropping it registers no languages and makes every
    /// report unresolvable.
    /// </summary>
    public string[] RenderReadyLanguages { get; set; } = ["en"];

    /// <summary>
    /// Which render workdirs (<c>render_&lt;random&gt;/</c> under
    /// <see cref="ReportsTempDir"/>) are kept for local inspection instead of
    /// deleted on dispose — none, failed renders only, or every render. A kept
    /// workdir holds its <c>.tex</c>, the Quarto/Pandoc/lualatex logs, the
    /// generated figures, and the rendered report itself. Default
    /// <see cref="RenderWorkdirRetention.None"/>.
    /// </summary>
    /// <remarks>
    /// Double-gated on purpose: any non-<see cref="RenderWorkdirRetention.None"/>
    /// value is honoured only when <c>IWebHostEnvironment.IsDevelopment()</c> —
    /// a kept workdir holds the rendered report, i.e. surveillance data, so it
    /// must never be retained on a production instance regardless of this
    /// setting. Kept workdirs are not auto-reaped: a Development session
    /// accumulates them under <see cref="ReportsTempDir"/> until cleared by hand
    /// (each retained path is logged when it is kept), so
    /// <see cref="RenderWorkdirRetention.All"/> is a deliberate local-diagnostics
    /// choice.
    /// </remarks>
    public RenderWorkdirRetention RenderWorkdirRetention { get; set; }
}

/// <summary>
/// Selects which of <see cref="QuartoReportProducer"/>'s per-render workdirs
/// (<c>render_&lt;random&gt;/</c> under <see cref="ReportingOptions.ReportsTempDir"/>)
/// survive on dispose for local inspection. Honoured only in Development — see
/// <see cref="ReportingOptions.RenderWorkdirRetention"/>.
/// </summary>
public enum RenderWorkdirRetention
{
    /// <summary>Delete every render workdir on dispose (default).</summary>
    None,

    /// <summary>Keep a failed render's workdir; delete it on success.</summary>
    Failed,

    /// <summary>Keep every render workdir, whether it succeeded or failed.</summary>
    All,
}

/// <summary>
/// How the Surveillance-Toolkit reports source was acquired in the build
/// of this image. Reflected at runtime to decide whether neoipcr is
/// expected at the well-known dev path.
/// </summary>
public enum BuildMode
{
    /// <summary>Default: cloned from a git branch (mutable ref).</summary>
    GithubBranch,

    /// <summary>Cloned from a git tag (immutable ref).</summary>
    GithubTag,

    /// <summary>Workspace build: COPYed from a sibling checkout (editable).</summary>
    Workspace,
}
