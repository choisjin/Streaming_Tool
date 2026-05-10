using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using StreamingHost.Signaling;

namespace StreamingHost;

public partial class MainWindow : Window
{
    private EmbeddedSignalingServer? _server;
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
        if (_server is not null) return; // disallow while running so existing viewers don't break
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
            _server = new EmbeddedSignalingServer(room, port);
            _server.ViewerJoined += OnViewerJoined;
            _server.ViewerLeft += vid => Log($"viewer left: {vid}");
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
            Log($"start failed: {ex.Message}");
            StartButton.IsEnabled = true;
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        if (_server is not null)
        {
            await _server.DisposeAsync();
            _server = null;
        }
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

    private void CopyLan_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(LanUrlBox.Text)) Clipboard.SetText(LanUrlBox.Text);
    }

    private void CopyWan_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(WanUrlBox.Text)) Clipboard.SetText(WanUrlBox.Text);
    }

    private void OnViewerJoined(ViewerSession session)
    {
        Log($"viewer joined: {session.ViewerId}");
        // Phase 2 will hook the WebRTC peer here.
        // For Phase 1 we just log the relayed messages so the round-trip can be verified.
        session.Message += (type, _) => Log($"viewer {session.ViewerId}: {type}");
    }

    private static string GetLanIPv4()
    {
        try
        {
            // Pick the IPv4 address on the active up interface that has a default gateway
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
