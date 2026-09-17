using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace PasswordManager.Desktop.Services;

public class IpcService
{
    private readonly MainWindow _window;
    private CancellationTokenSource? _cts;
    private const string PipeName = "FennecGuard_IPC_Pipe";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IpcService(MainWindow window)
    {
        _window = window;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipeServer.WaitForConnectionAsync(token);

                using var reader = new StreamReader(pipeServer, Encoding.UTF8);
                await using var writer = new StreamWriter(pipeServer, Encoding.UTF8) { AutoFlush = true };

                string? requestJson = await reader.ReadLineAsync(token);
                if (!string.IsNullOrEmpty(requestJson))
                {
                    string responseJson = await ProcessIpcCommandAsync(requestJson);
                    // Ensure response is framed on a single line for named pipe transmission
                    await writer.WriteLineAsync(responseJson.Replace("\r", "").Replace("\n", " "));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(100, token);
            }
        }
    }

    private async Task<string> ProcessIpcCommandAsync(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string action = root.GetProperty("action").GetString() ?? "";

            switch (action)
            {
                case "GET_STATUS":
                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        isUnlocked = _window.IsVaultUnlocked,
                        hasVault = _window.HasVaultInitialized
                    }, JsonOptions);

                case "UNLOCK":
                    string password = root.GetProperty("password").GetString() ?? "";
                    bool unlocked = await _window.UnlockFromIpcAsync(password);
                    return JsonSerializer.Serialize(new { success = unlocked }, JsonOptions);

                case "LOCK":
                    await _window.LockFromIpcAsync();
                    return JsonSerializer.Serialize(new { success = true }, JsonOptions);

                case "GET_LOGINS":
                    if (!_window.IsVaultUnlocked)
                    {
                        return JsonSerializer.Serialize(new { success = false, error = "Vault locked" }, JsonOptions);
                    }
                    var logins = await _window.GetLoginsForIpcAsync();
                    return JsonSerializer.Serialize(new { success = true, items = logins }, JsonOptions);

                case "GET_CREDENTIAL":
                    if (!_window.IsVaultUnlocked)
                    {
                        return JsonSerializer.Serialize(new { success = false, error = "Vault locked" }, JsonOptions);
                    }
                    string id = root.GetProperty("id").GetString() ?? "";
                    var cred = await _window.GetDecryptedCredentialForIpcAsync(id);
                    return JsonSerializer.Serialize(new { success = cred != null, credential = cred }, JsonOptions);

                case "SAVE":
                    if (!_window.IsVaultUnlocked)
                    {
                        return JsonSerializer.Serialize(new { success = false, error = "Vault locked" }, JsonOptions);
                    }
                    string title = root.GetProperty("title").GetString() ?? "";
                    string username = root.GetProperty("username").GetString() ?? "";
                    string url = root.GetProperty("url").GetString() ?? "";
                    string pass = root.GetProperty("password").GetString() ?? "";

                    bool saved = await _window.SaveCredentialFromIpcAsync(title, username, url, pass);
                    return JsonSerializer.Serialize(new { success = saved }, JsonOptions);

                default:
                    return JsonSerializer.Serialize(new { success = false, error = "Unknown action" }, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }
}
