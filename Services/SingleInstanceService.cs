using System.IO;
using System.IO.Pipes;
using System.Text;

namespace PortDeck.Services;

/// <summary>
/// Keeps a single PortDeck process running and forwards a second launch to the
/// primary instance through a local named pipe.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _listenerThread;

    public SingleInstanceService(string mutexName, string pipeName)
    {
        _pipeName = pipeName;
        _mutex = new Mutex(true, mutexName, out var createdNew);
        _ownsMutex = createdNew;
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

    public bool SignalExisting()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                client.Connect(600);
                using var writer = new StreamWriter(client, new UTF8Encoding(false));
                writer.WriteLine("activate");
                writer.Flush();
                return true;
            }
            catch (Exception) when (attempt < 5)
            {
                Thread.Sleep(120);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    public void StartListening(Action onActivate)
    {
        if (!IsFirstInstance || _listenerThread is not null) return;

        _listenerThread = new Thread(() => ListenLoop(onActivate))
        {
            IsBackground = true,
            Name = "PortDeck single-instance listener"
        };
        _listenerThread.Start();
    }

    private void ListenLoop(Action onActivate)
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                server.WaitForConnectionAsync(_cancellation.Token).GetAwaiter().GetResult();

                using var reader = new StreamReader(server, Encoding.UTF8);
                var message = reader.ReadLine();
                if (string.Equals(message, "activate", StringComparison.Ordinal))
                    onActivate();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (IOException)
            {
                // A client can disappear while connecting; keep the listener alive.
            }
            catch
            {
                // Never let the listener thread take down the process.
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }

        _mutex.Dispose();
        _cancellation.Dispose();
    }
}
