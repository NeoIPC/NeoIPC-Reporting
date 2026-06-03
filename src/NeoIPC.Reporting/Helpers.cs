namespace NeoIPC.Reporting;

/// <summary>Cross-cutting utilities used across multiple report generators.</summary>
public static class Helpers
{
    /// <summary>
    /// Maps an output media type to the conventional download-file
    /// extension. Used to compose <c>Content-Disposition</c> filenames
    /// (e.g. <c>NeoIPC-Surveillance-Reference-Report_…</c>).
    /// </summary>
    public static string FileExtensionFromMediaType(string mediaType)
    {
        return mediaType switch
        {
            "application/pdf" => ".pdf",
            "application/json" => ".json",
            "text/html" => ".html",
            _ => throw new NotSupportedException()
        };
    }
}
