using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace MdReader.App.Services;

/// <summary>
/// Single instance with tabs (§3.4): a named mutex decides ownership and a
/// named pipe hands the file path (and mode) to the running instance.
///
/// The classic race — the owner exits between our mutex check and our pipe
/// connect — is handled by retrying the whole sequence: if the pipe connect
/// fails we try to take the mutex again, and only give up into standalone mode
/// after a few rounds.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // Local\ scope: per interactive session, which is the right boundary for
    // a per-user document reader (two RDP users get their own instances).
    // MDREADER_INSTANCE_ID isolates test runs from the user's real instance.
    private static readonly string Suffix =
        Environment.GetEnvironmentVariable("MDREADER_INSTANCE_ID") is { Length: > 0 } id ? "-" + id : string.Empty;

    private static string MutexName => @"Local\mdreader-single-instance" + Suffix;
    private static string PipeName => "mdreader-activation" + Suffix;

    private Mutex? _mutex;
    private CancellationTokenSource? _serverCts;

    public sealed record Activation(string? FilePath, bool OpenInSource);

    public event EventHandler<Activation>? Activated;

    /// <summary>
    /// Tries to become the owning instance. Returns true when this process owns
    /// the app; false when the arguments were handed to an existing instance
    /// (the caller should exit).
    /// </summary>
    public bool TryBecomeOwner(Activation activation)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew)
            {
                _mutex = mutex;
                StartServer();
                return true;
            }

            mutex.Dispose();

            if (TrySendToOwner(activation))
            {
                return false;
            }

            // Owner is gone or unresponsive; brief pause, then race for the
            // mutex again.
            Thread.Sleep(150);
        }

        // Could neither own nor hand off — open standalone rather than failing.
        DiagLog.Write("single-instance: falling back to standalone window");
        return true;
    }

    private static bool TrySendToOwner(Activation activation)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 2000);
            using var writer = new StreamWriter(client);
            writer.WriteLine(JsonSerializer.Serialize(activation));
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void StartServer()
    {
        _serverCts = new CancellationTokenSource();
        var token = _serverCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync(token);
                    if (line is not null)
                    {
                        var activation = JsonSerializer.Deserialize<Activation>(line);
                        if (activation is not null)
                        {
                            Activated?.Invoke(this, activation);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    DiagLog.Write($"single-instance server error: {ex.Message}");
                    // Loop continues with a fresh pipe.
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _serverCts?.Cancel();
        _serverCts?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
