using System.Text.RegularExpressions;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// Disk-backed key/value store for binary resources (reference datasets,
/// validation-exception files, …) keyed by an opaque, format-validated id.
/// </summary>
/// <remarks>
/// Each entry is two files in <see cref="Root"/>:
/// <list type="bullet">
///   <item><description><c>{id}.{DataExtension}</c> — the payload bytes.</description></item>
///   <item><description><c>{id}.meta.json</c> — the JSON sidecar with display name, content type, size, uploader id, etc.</description></item>
/// </list>
/// Uploads use the <see cref="StageAsync"/> → <see cref="CommitAsync"/>
/// → <see cref="Discard"/> lifecycle so that crashes mid-upload never
/// leave a half-written entry visible to listings (the meta-sidecar is
/// the marker of presence — <see cref="Exists"/> only checks the
/// sidecar).
///
/// Ids are produced by <see cref="GenerateId"/> as 32 lowercase hex
/// characters (UUIDv7 in <c>"n"</c> format), and validated by
/// <see cref="IsValidId"/> before any path resolution. This keeps
/// path-traversal patterns from ever reaching the filesystem layer:
/// the id is the only caller-supplied component of any path the
/// storage layer constructs.
/// </remarks>
public abstract partial class FileStorage
{
    // \A and \z (not ^ and $) — \z anchors to end-of-string with no
    // exception for a trailing newline. .NET regex's $ matches both
    // end-of-string AND just before a final \n, which would let an
    // input like "32hex\n" slip through this guard. Same for ^ vs \A
    // for symmetry / belt-and-braces.
    [GeneratedRegex(@"\A[0-9a-f]{32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdRegexFactory();

    static readonly Regex s_idRegex = IdRegexFactory();

    /// <summary>The directory holding all entries for this resource type.</summary>
    public string Root { get; }

    /// <summary>The extension applied to data files (e.g. <c>"json"</c>, <c>"csv"</c>).</summary>
    public string DataExtension { get; }

    protected FileStorage(string root, string dataExtension)
    {
        Root = root;
        DataExtension = dataExtension.TrimStart('.');
    }

    /// <summary>Generates a fresh id (UUIDv7, 32 hex chars).</summary>
    public static string GenerateId() => Guid.CreateVersion7().ToString("n");

    /// <summary>
    /// Returns whether <paramref name="id"/> matches the storage id
    /// format. Always call this before passing user input to
    /// <see cref="DataPath"/>, <see cref="MetaPath"/>, or
    /// <see cref="Exists"/>.
    /// </summary>
    public static bool IsValidId(string id) => s_idRegex.IsMatch(id);

    /// <summary>Resolves the data-file path for <paramref name="id"/>.</summary>
    public string DataPath(string id)
    {
        if (!IsValidId(id))
            throw new ArgumentException("Invalid id format.", nameof(id));
        return Path.Combine(Root, $"{id}.{DataExtension}");
    }

    /// <summary>Resolves the metadata-sidecar path for <paramref name="id"/>.</summary>
    public string MetaPath(string id)
    {
        if (!IsValidId(id))
            throw new ArgumentException("Invalid id format.", nameof(id));
        return Path.Combine(Root, $"{id}.meta.json");
    }

    /// <summary>True iff a metadata sidecar exists for <paramref name="id"/>.</summary>
    public bool Exists(string id) => File.Exists(MetaPath(id));

    /// <summary>Yields the id of every entry whose metadata sidecar is present.</summary>
    public IEnumerable<string> EnumerateIds()
    {
        if (!Directory.Exists(Root)) yield break;
        foreach (var path in Directory.EnumerateFiles(Root, "*.meta.json"))
        {
            var name = Path.GetFileName(path);
            const string suffix = ".meta.json";
            if (!name.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var id = name[..^suffix.Length];
            if (IsValidId(id)) yield return id;
        }
    }

    /// <summary>Removes both files of an entry. No-op when missing.</summary>
    public void Delete(string id)
    {
        var data = DataPath(id);
        var meta = MetaPath(id);
        if (File.Exists(data)) File.Delete(data);
        if (File.Exists(meta)) File.Delete(meta);
    }

    /// <summary>
    /// Streams <paramref name="content"/> to a per-upload staging file in
    /// <see cref="Root"/> and returns its path.
    /// </summary>
    /// <remarks>
    /// The caller may inspect the staged file (e.g. extract metadata via
    /// an external process) before deciding the final id and sidecar
    /// shape, then call <see cref="CommitAsync"/> to publish the entry —
    /// or <see cref="Discard"/> to roll back.
    /// </remarks>
    public async Task<string> StageAsync(Stream content, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var stagedPath = Path.Combine(Root, $"staging-{Guid.NewGuid():n}.tmp");
        await using var fs = File.Create(stagedPath);
        await content.CopyToAsync(fs, ct);
        return stagedPath;
    }

    /// <summary>
    /// Atomically publishes the staged data and a freshly written sidecar
    /// under <paramref name="id"/>. The sidecar is written to a tmp file
    /// first; both <see cref="File.Move(string, string, bool)"/> calls
    /// are then atomic on POSIX, so a crash mid-commit cannot leave a
    /// half-published entry visible to <see cref="EnumerateIds"/>.
    /// </summary>
    public async Task CommitAsync(
        string id, string stagedDataPath, string sidecarJson, CancellationToken ct)
    {
        if (!IsValidId(id))
            throw new ArgumentException("Invalid id format.", nameof(id));

        var dataPath = DataPath(id);
        var metaPath = MetaPath(id);
        var tmpMeta = metaPath + ".tmp";

        await File.WriteAllTextAsync(tmpMeta, sidecarJson, ct);
        try
        {
            File.Move(stagedDataPath, dataPath, overwrite: true);
            File.Move(tmpMeta, metaPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmpMeta)) File.Delete(tmpMeta);
            throw;
        }
    }

    /// <summary>Removes a staged file when the upload is being abandoned.</summary>
    public void Discard(string stagedDataPath)
    {
        if (File.Exists(stagedDataPath)) File.Delete(stagedDataPath);
    }
}
