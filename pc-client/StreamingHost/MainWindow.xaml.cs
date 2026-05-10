using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using StreamingHost.Capture;
using StreamingHost.Signaling;
using StreamingHost.Streaming;

namespace StreamingHost;

public partial class MainWindow : Window
{
    private EmbeddedSignalingServer? _server;
    private StreamingPipeline? _pipeline;
    private DesktopCapture? _capture;
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(4) };

    public MainWindow()
    {
        InitializeComponent();
        RoomCodeBox.Text = GenerateCode();
    }

    private static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        Span<char> buf = stackalloc char[6];
        for (var i = 0; i < buf.Length; i++) buf[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        return new string(buf);
    }

    private void Log(string line) => Dispatcher.Invoke(() =>
    {
        LogList.Items.Add($"{DateTime.Now:HH:mm:ss} {line}");
        if (LogList.Items.Count > 0)
            LogList.ScrollIntoView(LogList.Items[^1]);
    });

    private void RegenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_server is not null) return;
        RoomCodeBox.Text = GenerateCode();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
        {
            StatusText.Text = "Invalid port"; return;
        }
        var room = RoomCodeBox.Text.Trim();
        if (room.Length < 4)
        {
            StatusText.Text = "Room code too short"; return;
        }

        StartButton.IsEnabled = false;
        try
        {
            // 1) Start desktop capture (full primary monitor for now; per-window in Phase 3)
            Log("[1/3] starting desktop capture…");
            _capture = new DesktopCapture(monitorIndex: 0);
            _capture.Start();
            Log($"capture started: {_capture.Width}x{_capture.Height}");

            // 2) Build pipeline (encoder is created with capture's resolution)
            Log("[2/3] starting pipeline + encoder…");
            _pipeline = new StreamingPipeline(_capture, fps: 60);
            _pipeline.Start();
            Log("pipeline ready");

            // 3) Embedded signaling server
            Log("[3/3] starting signaling server…");
            _server = new EmbeddedSignalingServer(room, port);
            _server.ViewerJoined += OnViewerJoined;
            _server.ViewerLeft += OnViewerLeft;
            _server.ServerError += err => Log($"server error: {err}");
            await _server.StartAsync();

            var lan = GetLanIPv4();
            LanUrlBox.Text = $"ws://{lan}:{port}/ws  (room: {room})";
            WanUrlBox.Text = "(resolving public IP…)";
            _ = ResolveWanAsync(port, room);

            StopButton.IsEnabled = true;
            RegenButton.IsEnabled = false;
            PortBox.IsEnabled = false;
            RoomCodeBox.IsEnabled = false;
            StatusText.Text = $"Listening on :{port}  room={room}";
            StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            Log($"signaling listening on :{port}  room={room}  lan={lan}");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed: {ex.Message}";
            StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            Log($"start failed: {ex.GetType().Name}: {ex.Message}");
            // Walk inner exceptions — SIPSorcery / Kestrel often wrap the real cause
            var inner = ex.InnerException;
            while (inner is not null)
            {
                Log($"  caused by {inner.GetType().Name}: {inner.Message}");
                inner = inner.InnerException;
            }
            // First few stack frames for the original throw
            foreach (var line in (ex.StackTrace ?? "").Split('\n').Take(4))
                Log("  " + line.Trim());
            await TeardownAsync();
            StartButton.IsEnabled = true;
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        await TeardownAsync();
        LanUrlBox.Text = "";
        WanUrlBox.Text = "";
        StatusText.Text = "Idle";
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StartButton.IsEnabled = true;
        RegenButton.IsEnabled = true;
        PortBox.IsEnabled = true;
        RoomCodeBox.IsEnabled = true;
        Log("stopped");
    }

    private async Task TeardownAsync()
    {
        if (_server is not null) { await _server.DisposeAsync(); _server = null; }
        _pipeline?.Dispose(); _pipeline = null;
        _capture?.Dispose(); _capture = null;
    }

    private void CopyLan_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(LanUrlBox.Text)) Clipboard.SetText(LanUrlBox.Text);
    }

    private void CopyWan_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(WanUrlBox.Text)) Clipboard.SetText(WanUrlBox.Text);
    }

    private async void OnViewerJoined(ViewerSession session)
    {
        Log($"viewer joined: {session.ViewerId}");
        try
        {
            var peer = new WebRtcPeer(session);
            peer.ConnectionStateChanged += s => Log($"viewer {session.ViewerId} state={s}");
            peer.InputMessageReceived += text => Log($"input[{session.ViewerId}]: {text}");
            _pipeline?.AddPeer(peer);
            await peer.StartOfferAsync();
        }
        catch (Exception ex)
        {
            Log($"peer for {session.ViewerId} failed: {ex.Message}");
        }
    }

    private void OnViewerLeft(string viewerId)
    {
        Log($"viewer left: {viewerId}");
        _pipeline?.RemovePeer(viewerId);
    }

    private static string GetLanIPv4()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var ipProps = ni.GetIPProperties();
                if (!ipProps.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                    continue;
                var addr = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork &&
                                         !IPAddress.IsLoopback(a.Address))?.Address;
                if (addr is not null) return addr.ToString();
            }
        }
        catch { }
        return "127.0.0.1";
    }

    private async Task ResolveWanAsync(int port, string room)
    {
        try
        {
            var ip = (await s_http.GetStringAsync("https://api.ipify.org")).Trim();
            Dispatcher.Invoke(() =>
            {
                WanUrlBox.Text = $"ws://{ip}:{port}/ws  (room: {room})  — forward TCP {port} on your router";
            });
        }
        catch
        {
            Dispatcher.Invoke(() => WanUrlBox.Text = "(public IP lookup failed — check internet)");
        }
    }
}
