using System.ComponentModel;
using System.Diagnostics;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The image the <c>Category=Container</c> fixtures run: the tag
/// <c>NEOIPC_REPORTING_IMAGE_TAG</c> names when it is set, otherwise
/// <c>neoipc-reporting:smoke-test</c>, built from this repository's Dockerfile
/// by the test run itself.
/// </summary>
/// <remarks>
/// <para>
/// Building here is what lets a plain <c>dotnet test</c> or a Test Explorer run
/// exercise the image with no separate step to remember. The build runs on every
/// such run, not only when the tag is missing: an image left over from an earlier
/// build tests the sources as they were then, and a fixture that reused it would
/// report on code that no longer exists. That currency is this repository's own:
/// the Surveillance-Toolkit clone and the neoipcr install are cached layers that
/// stay at whatever <c>main</c> was when the cache entry was made, until the
/// builder cache is pruned. BuildKit's layer cache keeps an unchanged rebuild to
/// seconds; the first build on a machine fetches the R, TeX Live and Quarto
/// toolchains and the report sources from GitHub, and takes tens of minutes.
/// Naming a tag opts out of the build: the CI smoke job builds with its own inputs
/// and names the result, and a developer can point at any built image the same way.
/// </para>
/// <para>
/// No Docker to talk to is a skip, as the fixtures treat an unreachable daemon; a
/// build that fails is a failure, since the image is what the category verifies.
/// </para>
/// </remarks>
static class SmokeTestImage
{
    const string DefaultTag = "neoipc-reporting:smoke-test";
    const string DockerfilePath = "src/NeoIPC.Reporting/Dockerfile";
    const string SolutionFileName = "NeoIPC-Reporting.sln";

    static readonly SemaphoreSlim Gate = new(1, 1);
    static Task? _build;

    /// <summary>
    /// Returns the tag to run, building it first when <c>NEOIPC_REPORTING_IMAGE_TAG</c>
    /// names none. Ignores the calling fixture when Docker is unavailable.
    /// </summary>
    public static async Task<string> ResolveAsync()
    {
        var configured = Environment.GetEnvironmentVariable("NEOIPC_REPORTING_IMAGE_TAG");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        // Both container fixtures resolve the image. The build's task is kept, so the
        // second fixture awaits the first's outcome — a failure or a skip included —
        // rather than running the build again and reporting the same failure twice.
        await Gate.WaitAsync();
        try
        {
            _build ??= BuildAsync();
        }
        finally
        {
            Gate.Release();
        }

        await _build;
        return DefaultTag;
    }

    static async Task BuildAsync()
    {
        var progress = TestContext.Progress;

        // The probe needs no checkout, so it runs first: a machine without Docker is a
        // skip whether or not the binaries sit inside a repository.
        var daemon = await RunDockerAsync(
            AppContext.BaseDirectory, ["version", "--format", "{{.Server.Version}}"], _ => { });
        if (daemon is null)
            Assert.Ignore("Category=Container tests need Docker: no `docker` command was found on PATH.");
        if (daemon.ExitCode != 0)
            Assert.Ignore(
                "Category=Container tests need a running Docker daemon: `docker version` could not " +
                $"reach one. {daemon.LastLines}");

        var root = RepositoryRoot();
        progress.WriteLine(
            $"Building {DefaultTag} from {DockerfilePath} (BuildKit cache; the first build on a " +
            "machine takes tens of minutes)...");
        var build = await RunDockerAsync(root, ["build", "-f", DockerfilePath, "-t", DefaultTag, "."],
            progress.WriteLine);
        if (build is null || build.ExitCode != 0)
            Assert.Fail(
                $"`docker build` of {DefaultTag} exited {build?.ExitCode}. Last output:\n{build?.LastLines}");
    }

    /// <summary>
    /// The directory holding the solution file, found by walking up from the test
    /// assembly, so the build context is the same whether the run starts in an IDE
    /// or from <c>dotnet test</c> in any directory.
    /// </summary>
    static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName))) return dir.FullName;
        }

        throw new InvalidOperationException(
            $"{SolutionFileName} was not found above {AppContext.BaseDirectory}; the container tests " +
            "build the image from the repository root and need to run from a checkout.");
    }

    sealed record DockerResult(int ExitCode, string LastLines);

    /// <summary>
    /// Runs <c>docker</c> with <paramref name="arguments"/>, forwarding every output
    /// line to <paramref name="onLine"/> as it arrives and keeping the last lines for
    /// a failure message. Returns null when no <c>docker</c> executable exists.
    /// </summary>
    static async Task<DockerResult?> RunDockerAsync(
        string workingDirectory, string[] arguments, Action<string> onLine)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        // The Dockerfile's `# syntax=` directive needs BuildKit, which every current
        // Docker enables by default; naming it keeps a `DOCKER_BUILDKIT=0` in the
        // caller's environment from handing the build to the classic builder.
        startInfo.Environment["DOCKER_BUILDKIT"] = "1";

        using var process = new Process { StartInfo = startInfo };
        var tail = new Queue<string>();

        void Capture(string? line)
        {
            if (line is null) return;
            onLine(line);
            lock (tail)
            {
                tail.Enqueue(line);
                if (tail.Count > 40) tail.Dequeue();
            }
        }

        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);
        try
        {
            process.Start();
        }
        // Only "not found" means there is no docker to run: code 2 is ENOENT on Unix and
        // ERROR_FILE_NOT_FOUND on Windows, 3 is ERROR_PATH_NOT_FOUND. Any other start
        // failure — a docker that is not executable, access denied — propagates and
        // fails the fixture with its own message instead of being reported as a skip.
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            return null;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        lock (tail)
        {
            return new DockerResult(process.ExitCode, string.Join('\n', tail));
        }
    }
}
