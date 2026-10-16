using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

/// <summary>
/// Starts the playground application for the test run and stops it when the last class is done.
/// </summary>
/// <remarks>
/// <para>
/// The app is launched as a separate process on a free port rather than hosted in-process:
/// the canvas only exists once the WebAssembly client has booted and imported the real static
/// web assets, which is precisely what these tests are here to verify.
/// </para>
/// <para>
/// One process for the whole run, shared by every class that asks for it. A process per class meant
/// thirteen Blazor hosts starting at once — xUnit runs classes in parallel — and past about a dozen
/// they starve each other: a class times out waiting for its own app and every test in it fails
/// with a message about a port, while nothing is wrong with the code under test. Sharing is safe
/// because the isolation that matters comes from the browser: every test drives its own page, and
/// the demo re-reads its sample on each load, so no document state crosses between them.
/// </para>
public sealed class DemoAppFixture : IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static Process? _process;
    private static ConcurrentQueue<string> _startupErrors = new();
    private static string _baseUrl = string.Empty;
    private static int _users;

    /// <summary>
    /// The build configuration these tests were compiled in, read off their own location.
    /// </summary>
    /// <remarks>
    /// The test assembly sits in <c>bin/&lt;configuration&gt;/&lt;framework&gt;</c>, so its own path
    /// is the one thing that cannot disagree with how it was built. A compile-time <c>#if DEBUG</c>
    /// would say the same thing, but only for this assembly, and would go stale the moment the
    /// launch needed anything else from the build.
    /// </remarks>
    private static string Configuration =>
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    /// <summary>The base address the demo is listening on.</summary>
    /// <remarks>
    /// An instance member although the process behind it is shared: it is what the injected classes
    /// read, and making it static would change every call site to say nothing more than it says now.
    /// </remarks>
#pragma warning disable CA1822 // Mark members as static
    public string BaseUrl => _baseUrl;
#pragma warning restore CA1822

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);

        try
        {
            _users++;
            if (_process is { HasExited: false }) return;

            await StartAsync().ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task StartAsync()
    {
        var port = FindFreePort();
        _baseUrl = $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}";

        var projectPath = Path.Combine(FindRepositoryRoot(), "playground", "Bpmn.Demo", "Bpmn.Demo.csproj");

        /*
         * The configuration is passed explicitly, and that is not a detail. `dotnet run` defaults
         * to Debug, so a suite run with `-c Release` used to launch whatever happened to be sitting
         * in the demo's Debug output — which could be hours old. The tests then passed or failed
         * against code that was not the code under test, which is the worst way for a test suite to
         * be wrong: quietly, and in both directions.
         */
        var startInfo = new ProcessStartInfo("dotnet")
        {
            // --no-build: the test project references the demo, so the run that built these tests
            // built it too, in this same configuration. Building here would race that output.
            Arguments = $"run --project \"{projectPath}\" --configuration {Configuration} --no-build --no-launch-profile",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.Environment["ASPNETCORE_URLS"] = _baseUrl;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the playground application.");

        // An undrained redirected pipe eventually blocks host logging and stalls requests.
        var errors = new ConcurrentQueue<string>();
        _startupErrors = errors;
        _process.ErrorDataReceived += (sender, args) =>
        {
            if (args.Data is not { } line) return;
            errors.Enqueue(line);
            while (errors.Count > 100) errors.TryDequeue(out _);
        };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilRespondingAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);

        try
        {
            // The last class out turns off the lights. Counting rather than leaving it running
            // keeps the app from outliving the run that started it.
            _users--;
            if (_users > 0) return;

            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(10_000);
            }

            _process?.Dispose();
            _process = null;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task WaitUntilRespondingAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
            {
                var error = string.Join(Environment.NewLine, _startupErrors);
                throw new InvalidOperationException($"The playground application exited during startup. {error}");
            }

            try
            {
                using var response = await client.GetAsync(new Uri(_baseUrl)).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException)
            {
                // Still starting up.
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        throw new TimeoutException($"The playground application did not start listening on {_baseUrl}.");
    }

    private static int FindFreePort()
    {
        // Bind to port 0 and read back what the OS assigned, so parallel runs cannot collide.
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "playground"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output folder.");
    }
}
