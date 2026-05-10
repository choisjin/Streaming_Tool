using System;
using System.Windows;
using StreamingHost.Signaling;

namespace StreamingHost;

public partial class MainWindow : Window
{
    private SignalingClient? _signaling;

    public MainWindow()
    {
        InitializeComponent();
        RoomCodeBox.Text = GenerateCode();
    }

    private static string GenerateCode()
    {
        var rng = Random.Shared;
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        Span<char> buf = stackalloc char[6];
        for (var i = 0; i < buf.Length; i++) buf[i] = alphabet[rng.Next(alphabet.Length)];
        return new string(buf);
    }

    private void Log(string line)
    {
        Dispatcher.Invoke(() =>
        {
            LogList.Items.Add($"{DateTime.Now:HH:mm:ss} {line}");
            LogList.ScrollIntoView(LogList.Items[^1]);
        });
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        try
        {
            var url = new Uri(SignalingUrlBox.Text);
            var room = RoomCodeBox.Text.Trim();
            _signaling = new SignalingClient(url, room);
            _signaling.ViewerJoined += vid => Log($"viewer joined: {vid}");
            _signaling.ViewerLeft += vid => Log($"viewer left: {vid}");
            _signaling.OfferAnswer += (type, _) => Log($"signaling: {type}");
            _signaling.IceReceived += (vid, _) => Log($"ice from {vid}");
            _signaling.Error += err => Log($"error: {err}");

            await _signaling.ConnectAndRegisterAsync();
            StatusText.Text = $"Registered as host in room {room}";
            StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            StopButton.IsEnabled = true;
            Log($"signaling connected, room={room}");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed: {ex.Message}";
            StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            Log($"connect failed: {ex.Message}");
            StartButton.IsEnabled = true;
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        if (_signaling is not null)
        {
            await _signaling.DisposeAsync();
            _signaling = null;
        }
        StatusText.Text = "Idle";
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StartButton.IsEnabled = true;
        Log("stopped");
    }
}
