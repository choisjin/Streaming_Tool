using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace StreamingHost.Input;

/// <summary>
/// Owns the SerialPort to the Pro Micro and serializes outbound commands on a
/// background writer task. Application code calls <see cref="SendLine"/> from
/// any thread; the writer pumps the channel as fast as the COM endpoint can
/// drain. The reader loop fires <see cref="LineReceived"/> for every '\n'-
/// terminated line (READY / PONG / ERR ...).
/// </summary>
public sealed class SerialBridge : IDisposable
{
    public string PortName { get; }
    public bool IsOpen => _port is { IsOpen: true };

    public event Action<string>? LineReceived;
    public event Action<string>? Diagnostic;

    private readonly Channel<string> _outbox = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private SerialPort? _port;
    private Task? _writeTask;
    private Task? _readTask;

    public SerialBridge(string portName) => PortName = portName;

    public void Open()
    {
        _port = new SerialPort(PortName, 115200, Parity.None, 8, StopBits.One)
        {
            DtrEnable = true,         // most USB-CDC devices need DTR for the host's open() to be visible
            RtsEnable = true,
            ReadTimeout = 50,
            WriteTimeout = 200,
            NewLine = "\n",
            Encoding = System.Text.Encoding.ASCII,
        };
        _port.Open();
        Diagnostic?.Invoke($"serial open {PortName}");

        _writeTask = Task.Run(WriteLoopAsync);
        _readTask  = Task.Run(ReadLoop);
    }

    /// <summary>Queue an ASCII command (without trailing '\n'); writer adds it.</summary>
    public void SendLine(string cmd)
    {
        if (string.IsNullOrEmpty(cmd)) return;
        _outbox.Writer.TryWrite(cmd);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var cmd in _outbox.Reader.ReadAllAsync(_cts.Token))
            {
                if (_port is not { IsOpen: true }) return;
                try
                {
                    _port.WriteLine(cmd);
                }
                catch (Exception ex)
                {
                    Diagnostic?.Invoke($"serial write failed: {ex.GetType().Name}: {ex.Message}");
                    // Drop and keep the port; the read loop will detect a hard fault.
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ReadLoop()
    {
        var sb = new System.Text.StringBuilder(64);
        while (!_cts.IsCancellationRequested && _port is { IsOpen: true })
        {
            try
            {
                var ch = _port.ReadChar();
                if (ch == '\n')
                {
                    var line = sb.ToString().TrimEnd('\r');
                    sb.Clear();
                    if (line.Length > 0) LineReceived?.Invoke(line);
                }
                else if (ch != '\r')
                {
                    sb.Append((char)ch);
                }
            }
            catch (TimeoutException) { /* expected — keep polling */ }
            catch (Exception ex)
            {
                Diagnostic?.Invoke($"serial read error: {ex.GetType().Name}: {ex.Message}");
                break;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _outbox.Writer.TryComplete(); } catch { }
        try { _writeTask?.Wait(500); } catch { }
        try { _port?.Close(); } catch { }
        _port?.Dispose();
        _port = null;
    }

    /// <summary>Best-effort detection of an Arduino-class CDC device (Leonardo / Pro Micro).</summary>
    public static string[] EnumeratePorts() => SerialPort.GetPortNames();
}
