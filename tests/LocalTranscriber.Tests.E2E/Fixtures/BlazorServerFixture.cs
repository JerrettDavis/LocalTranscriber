using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace LocalTranscriber.Tests.E2E.Fixtures;

public class BlazorServerFixture : IAsyncDisposable
{
    private Process? _process;
    private readonly List<string> _output = [];
    public string BaseUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var port = GetRandomPort();
        BaseUrl = $"http://localhost:{port}";

        // Resolve path to the Web project's built DLL
        var repoRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        var webDll = Path.Combine(repoRoot,
            "src", "LocalTranscriber.Web", "bin", "Release", "net10.0", "LocalTranscriber.Web.dll");

        if (!File.Exists(webDll))
        {
            // Fallback: try to build it
            var buildPsi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{Path.Combine(repoRoot, "src", "LocalTranscriber.Web", "LocalTranscriber.Web.csproj")}\" -c Release --no-restore",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var buildProc = Process.Start(buildPsi)!;
            await buildProc.WaitForExitAsync();
            if (!File.Exists(webDll))
                throw new FileNotFoundException($"Web project DLL not found at {webDll}");
        }

        // Content root must point to the Web project source so static assets are found
        var contentRoot = Path.Combine(repoRoot, "src", "LocalTranscriber.Web");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"exec \"{webDll}\" --urls \"{BaseUrl}\" --contentroot \"{contentRoot}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = contentRoot,
        };
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Blazor server process");

        // Drain stdout/stderr asynchronously to prevent buffer deadlock
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (_output) _output.Add($"[OUT] {e.Data}");
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (_output) _output.Add($"[ERR] {e.Data}");
        };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitForHealthAsync(TimeSpan.FromSeconds(120));
    }

    private async Task WaitForHealthAsync(TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            // Check if process crashed
            if (_process is { HasExited: true })
            {
                string logs;
                lock (_output) logs = string.Join("\n", _output.TakeLast(30));
                throw new InvalidOperationException(
                    $"Blazor server process exited with code {_process.ExitCode}.\nLast output:\n{logs}");
            }

            try
            {
                var resp = await http.GetAsync($"{BaseUrl}/api/health");
                if (resp.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch
            {
                // Server not ready yet
            }
            await Task.Delay(500);
        }

        string finalLogs;
        lock (_output) finalLogs = string.Join("\n", _output.TakeLast(30));
        throw new TimeoutException(
            $"Blazor server did not become healthy within {timeout.TotalSeconds}s at {BaseUrl}.\nLast output:\n{finalLogs}");
    }

    private static int GetRandomPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            catch { }
        }
        _process?.Dispose();
    }
}
