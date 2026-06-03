namespace NeoIPC.Reporting;

/// <summary>
/// Contract every report-rendering pipeline implements: produce a
/// <see cref="DataResult"/>, then dispose any per-render resources
/// (the symlink-forest workdir, staged temp files, …) on
/// <see cref="IAsyncDisposable.DisposeAsync"/>.
/// </summary>
interface IDataGenerator : IAsyncDisposable
{
    Task<DataResult> Generate(CancellationToken cancellationToken);
}
