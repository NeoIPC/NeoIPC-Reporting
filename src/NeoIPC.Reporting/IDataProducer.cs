namespace NeoIPC.Reporting;

/// <summary>
/// Contract every report-rendering pipeline implements: produce a
/// <see cref="DataResult"/>, then dispose any per-render resources
/// (the symlink-forest workdir, staged temp files, …) on
/// <see cref="IAsyncDisposable.DisposeAsync"/>.
/// </summary>
interface IDataProducer : IAsyncDisposable
{
    /// <summary>
    /// The media type the generator emits. Used by the endpoint layer
    /// to decide whether response post-processing (e.g.
    /// <c>fragmentMode</c> for HTML) applies.
    /// </summary>
    string MediaType { get; }

    Task<DataResult> Generate(CancellationToken cancellationToken);
}
