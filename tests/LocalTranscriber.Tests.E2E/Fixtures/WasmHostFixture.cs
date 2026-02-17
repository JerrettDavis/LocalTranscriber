using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace LocalTranscriber.Tests.E2E.Fixtures;

public class WasmHostFixture : IAsyncDisposable
{
    private WebApplication? _app;
    public string BaseUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var repoRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        // Try build output wwwroot first (faster, no publish needed)
        var wwwrootPath = Path.Combine(repoRoot,
            "src", "LocalTranscriber.Web.Client", "bin", "Release", "net10.0", "wwwroot");

        // Check for index.html specifically — Blazor build creates the _framework/
        // folder in wwwroot but only publish copies static assets like index.html
        if (!File.Exists(Path.Combine(wwwrootPath, "index.html")))
        {
            // Fallback: try publish
            var projectPath = Path.Combine(repoRoot,
                "src", "LocalTranscriber.Web.Client", "LocalTranscriber.Web.Client.csproj");

            var publishDir = Path.Combine(Path.GetTempPath(), "LocalTranscriber.Tests.E2E", "wasm-publish");

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish \"{projectPath}\" -c Release -o \"{publishDir}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var publishProcess = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start publish process");

            // Drain output to prevent deadlock
            var stdoutTask = publishProcess.StandardOutput.ReadToEndAsync();
            var stderrTask = publishProcess.StandardError.ReadToEndAsync();
            await publishProcess.WaitForExitAsync();
            var stderr = await stderrTask;

            if (publishProcess.ExitCode != 0)
                throw new InvalidOperationException($"WASM publish failed (exit {publishProcess.ExitCode}): {stderr}");

            wwwrootPath = Path.Combine(publishDir, "wwwroot");
        }

        if (!Directory.Exists(wwwrootPath))
            throw new DirectoryNotFoundException($"WASM wwwroot not found at {wwwrootPath}");

        // Check that index.html exists
        var indexPath = Path.Combine(wwwrootPath, "index.html");
        if (!File.Exists(indexPath))
            throw new FileNotFoundException($"index.html not found at {indexPath}");

        var port = GetRandomPort();
        BaseUrl = $"http://localhost:{port}";

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(BaseUrl);

        _app = builder.Build();

        var fileProvider = new PhysicalFileProvider(wwwrootPath);
        _app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        _app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            // Blazor WASM uses .dat (ICU), .blat, and other file types that
            // aren't in the default content type provider
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream"
        });
        _app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = fileProvider });

        await _app.StartAsync();
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
        if (_app is not null)
            await _app.DisposeAsync();
    }
}
