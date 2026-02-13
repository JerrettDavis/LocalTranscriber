using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace LocalTranscriber.Cli.Services;

/// <summary>
/// Provides resilient HTTP functionality with retry logic and enterprise network support.
/// </summary>
internal static class ResilientHttp
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8)
    ];

    /// <summary>
    /// Creates an HttpClient configured for enterprise environments.
    /// </summary>
    public static HttpClient CreateClient(bool trustAllCerts = false, TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = true,
            UseDefaultCredentials = true
        };

        if (trustAllCerts)
        {
            handler.ServerCertificateCustomValidationCallback = TrustAllCertificates;
            Console.WriteLine("[WARN] SSL certificate validation disabled - use only for testing!");
        }

        var client = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromMinutes(5)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalTranscriber/1.0");
        
        return client;
    }

    /// <summary>
    /// Downloads a file with retry logic and progress reporting.
    /// </summary>
    public static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        bool trustAllCerts = false,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            if (attempt > 0)
            {
                var delay = RetryDelays[attempt - 1];
                Console.WriteLine($"[Retry {attempt}/{RetryDelays.Length}] Waiting {delay.TotalSeconds}s before retry...");
                await Task.Delay(delay, ct);
            }

            try
            {
                using var client = CreateClient(trustAllCerts);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    lastError = new HttpRequestException($"503 Service Unavailable from {new Uri(url).Host}. " +
                        "This may indicate proxy/firewall blocking or the service is temporarily down.");
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
                {
                    throw new HttpRequestException(
                        "407 Proxy Authentication Required. Configure your system proxy credentials or set " +
                        "HTTP_PROXY/HTTPS_PROXY environment variables with credentials.");
                }

                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var tempPath = destinationPath + ".tmp";

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = File.Create(tempPath);

                var buffer = new byte[81920];
                var bytesRead = 0L;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    bytesRead += read;

                    if (totalBytes > 0)
                        progress?.Report((double)bytesRead / totalBytes);
                }

                await fileStream.FlushAsync(ct);
                fileStream.Close();

                File.Move(tempPath, destinationPath, overwrite: true);
                return;
            }
            catch (HttpRequestException ex) when (IsRetryable(ex))
            {
                lastError = ex;
                Console.WriteLine($"[WARN] Download failed: {ex.Message}");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = new TimeoutException($"Download timed out for {url}");
                Console.WriteLine("[WARN] Download timed out");
            }
        }

        throw new HttpRequestException(
            $"Failed to download after {RetryDelays.Length + 1} attempts. Last error: {lastError?.Message}\n\n" +
            "Troubleshooting:\n" +
            "- Check network connectivity\n" +
            "- If behind a proxy, ensure HTTP_PROXY/HTTPS_PROXY are set\n" +
            "- If using enterprise SSL inspection, try --trust-all-certs\n" +
            "- Try manually downloading the model and placing it in the models directory",
            lastError);
    }

    /// <summary>
    /// Makes an HTTP request with retry logic.
    /// </summary>
    public static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken ct = default)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            if (attempt > 0)
            {
                var delay = RetryDelays[attempt - 1];
                await Task.Delay(delay, ct);
            }

            try
            {
                // Clone request for retry (request can only be sent once)
                using var clone = await CloneRequestAsync(request);
                var response = await client.SendAsync(clone, ct);

                if (response.StatusCode == HttpStatusCode.ServiceUnavailable ||
                    response.StatusCode == HttpStatusCode.BadGateway ||
                    response.StatusCode == HttpStatusCode.GatewayTimeout)
                {
                    lastError = new HttpRequestException($"{(int)response.StatusCode} from {request.RequestUri?.Host}");
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (IsRetryable(ex))
            {
                lastError = ex;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = new TimeoutException("Request timed out");
            }
        }

        throw new HttpRequestException(
            $"Request failed after {RetryDelays.Length + 1} attempts: {lastError?.Message}", lastError);
    }

    private static bool IsRetryable(HttpRequestException ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("503") ||
               message.Contains("502") ||
               message.Contains("504") ||
               message.Contains("timeout") ||
               message.Contains("connection") ||
               ex.InnerException is System.Net.Sockets.SocketException;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content != null)
        {
            var content = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(content);

            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private static bool TrustAllCertificates(
        HttpRequestMessage message,
        X509Certificate2? cert,
        X509Chain? chain,
        SslPolicyErrors errors) => true;
}
