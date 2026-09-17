using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PasswordManager.Desktop.Services;

public class LocalServerService
{
    private readonly MainWindow _window;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private const int Port = 41893;
    private const long MaxPayloadBytes = 64 * 1024; // 64 KB DoS protection ceiling

    // Rate limiting state for /unlock
    private int _failedAttempts = 0;
    private DateTime _lockoutUntil = DateTime.MinValue;

    private readonly byte[] _expectedTokenBytes;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LocalServerService(MainWindow window)
    {
        _window = window;

        string tokenFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "token.json");
        string token = "";
        if (File.Exists(tokenFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(tokenFile));
                token = doc.RootElement.GetProperty("token").GetString() ?? "";
            }
            catch {}
        }
        _expectedTokenBytes = Encoding.UTF8.GetBytes(token);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();

        Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = ProcessRequestAsync(context);
            }
            catch
            {
                if (token.IsCancellationRequested) break;
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;

        string? origin = req.Headers["Origin"];

        // Anti-CSRF: Reject external public web pages
        if (!string.IsNullOrEmpty(origin) && 
            (origin.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
             origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            res.StatusCode = (int)HttpStatusCode.Forbidden;
            res.Close();
            return;
        }

        res.Headers.Add("Access-Control-Allow-Origin", string.IsNullOrEmpty(origin) ? "*" : origin);
        res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-FennecGuard-Auth");

        if (req.HttpMethod == "OPTIONS")
        {
            res.StatusCode = 204;
            res.Close();
            return;
        }

        // DoS Protection: Reject payloads larger than 64 KB
        if (req.ContentLength64 > MaxPayloadBytes)
        {
            res.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
            res.Close();
            return;
        }

        // Validate Shared Secret Authentication Token (Constant-time check)
        string? incomingToken = req.Headers["X-FennecGuard-Auth"];
        byte[] incomingBytes = Encoding.UTF8.GetBytes(incomingToken ?? "");

        bool isValidToken = incomingBytes.Length == _expectedTokenBytes.Length &&
                            CryptographicOperations.FixedTimeEquals(incomingBytes, _expectedTokenBytes);

        if (!isValidToken)
        {
            res.StatusCode = (int)HttpStatusCode.Unauthorized;
            byte[] errBuf = Encoding.UTF8.GetBytes("{\"success\":false,\"error\":\"Unauthorized token\"}");
            res.ContentType = "application/json";
            res.ContentLength64 = errBuf.Length;
            await res.OutputStream.WriteAsync(errBuf);
            res.Close();
            return;
        }

        string path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "";
        string responseJson = "{\"success\":false,\"error\":\"Not found\"}";

        try
        {
            if (req.HttpMethod == "GET" && path == "/status")
            {
                responseJson = JsonSerializer.Serialize(new
                {
                    success = true,
                    isUnlocked = _window.IsVaultUnlocked,
                    hasVault = _window.HasVaultInitialized
                }, JsonOptions);
            }
            else if (req.HttpMethod == "POST" && path == "/unlock")
            {
                // Rate Limiting Check: 5 attempts triggers 30s cooldown
                if (DateTime.UtcNow < _lockoutUntil)
                {
                    int secondsLeft = (int)(_lockoutUntil - DateTime.UtcNow).TotalSeconds;
                    responseJson = JsonSerializer.Serialize(new { success = false, error = $"Too many failed attempts. Wait {secondsLeft}s." }, JsonOptions);
                }
                else
                {
                    using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                    string body = await reader.ReadToEndAsync();
                    using var doc = JsonDocument.Parse(body);
                    string password = doc.RootElement.GetProperty("password").GetString() ?? "";

                    bool unlocked = await _window.UnlockFromIpcAsync(password);
                    if (unlocked)
                    {
                        _failedAttempts = 0;
                        responseJson = JsonSerializer.Serialize(new { success = true }, JsonOptions);
                    }
                    else
                    {
                        _failedAttempts++;
                        if (_failedAttempts >= 5)
                        {
                            _lockoutUntil = DateTime.UtcNow.AddSeconds(30);
                            _failedAttempts = 0;
                        }
                        responseJson = JsonSerializer.Serialize(new { success = false, error = "Incorrect password" }, JsonOptions);
                    }
                }
            }
            else if (req.HttpMethod == "POST" && path == "/lock")
            {
                await _window.LockFromIpcAsync();
                responseJson = JsonSerializer.Serialize(new { success = true }, JsonOptions);
            }
            else if (req.HttpMethod == "GET" && path == "/logins")
            {
                if (!_window.IsVaultUnlocked)
                {
                    responseJson = JsonSerializer.Serialize(new { success = false, error = "Vault locked" }, JsonOptions);
                }
                else
                {
                    var logins = await _window.GetLoginsForIpcAsync();
                    responseJson = JsonSerializer.Serialize(new { success = true, items = logins }, JsonOptions);
                }
            }
            else if (req.HttpMethod == "POST" && path == "/credential")
            {
                if (!_window.IsVaultUnlocked)
                {
                    responseJson = JsonSerializer.Serialize(new { success = false, error = "Vault locked" }, JsonOptions);
                }
                else
                {
                    using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                    string body = await reader.ReadToEndAsync();
                    using var doc = JsonDocument.Parse(body);
                    string id = doc.RootElement.GetProperty("id").GetString() ?? "";

                    var cred = await _window.GetDecryptedCredentialForIpcAsync(id);
                    responseJson = JsonSerializer.Serialize(new { success = cred != null, credential = cred }, JsonOptions);
                }
            }
            else if (req.HttpMethod == "POST" && path == "/save")
            {
                if (!_window.IsVaultUnlocked)
                {
                    responseJson = JsonSerializer.Serialize(new { success = false, error = "Vault locked" }, JsonOptions);
                }
                else
                {
                    using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                    string body = await reader.ReadToEndAsync();
                    using var doc = JsonDocument.Parse(body);
                    string title = doc.RootElement.GetProperty("title").GetString() ?? "";
                    string username = doc.RootElement.GetProperty("username").GetString() ?? "";
                    string url = doc.RootElement.GetProperty("url").GetString() ?? "";
                    string password = doc.RootElement.GetProperty("password").GetString() ?? "";

                    bool saved = await _window.SaveCredentialFromIpcAsync(title, username, url, password);
                    responseJson = JsonSerializer.Serialize(new { success = saved }, JsonOptions);
                }
            }
        }
        catch (Exception ex)
        {
            responseJson = JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }

        byte[] buffer = Encoding.UTF8.GetBytes(responseJson);
        res.ContentType = "application/json";
        res.ContentLength64 = buffer.Length;
        await res.OutputStream.WriteAsync(buffer);
        res.Close();
    }
}
