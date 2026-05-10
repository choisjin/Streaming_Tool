using System;
using System.Diagnostics;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace StreamingHost.Capture;

/// <summary>
/// Full-screen capture via DXGI Desktop Duplication (all monitors -> primary monitor only for now).
/// Suitable for non-protected content. For per-window capture see WindowCapture.
/// </summary>
public sealed class DesktopCapture : IFrameSource
{
    private readonly int _monitorIndex;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;
    private Thread? _thread;
    private volatile bool _running;
    private readonly Stopwatch _clock = new();

    public int Width { get; private set; }
    public int Height { get; private set; }
    public event FrameAvailableHandler? FrameAvailable;

    public DesktopCapture(int monitorIndex = 0)
    {
        _monitorIndex = monitorIndex;
    }

    public void Start()
    {
        Initialize();
        _running = true;
        _clock.Start();
        _thread = new Thread(Loop) { IsBackground = true, Name = "DesktopCapture" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(500);
    }

    private void Initialize()
    {
        // Pick primary adapter, monitor by index
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        using var adapter = factory.GetAdapter1(0);

        // Create D3D11 device
        D3D11.D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_0 },
            out _device).CheckError();

        _context = _device!.ImmediateContext;

        // Locate output (monitor)
        var outputCount = 0;
        IDXGIOutput? selectedOutput = null;
        for (var i = 0; ; i++)
        {
            if (adapter.EnumOutputs(i, out var o).Failure) break;
            if (i == _monitorIndex) selectedOutput = o;
            else o.Dispose();
            outputCount++;
        }
        if (selectedOutput is null) throw new InvalidOperationException($"Monitor {_monitorIndex} not found (count={outputCount}).");

        using var output1 = selectedOutput.QueryInterface<IDXGIOutput1>();
        var desc = selectedOutput.Description;
        Width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
        Height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;

        _duplication = output1.DuplicateOutput(_device);

        // Staging texture for CPU read-back
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = Width,
            Height = Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        });

        selectedOutput.Dispose();
    }

    private void Loop()
    {
        if (_duplication is null || _staging is null || _context is null) return;

        while (_running)
        {
            var hr = _duplication.AcquireNextFrame(50, out var frameInfo, out var resource);
            if (hr.Failure)
            {
                if (hr == ResultCode.WaitTimeout) continue;
                // Lost duplication (resolution change, mode switch). Reinit.
                ReinitializeQuiet();
                continue;
            }

            try
            {
                using var sourceTex = resource.QueryInterface<ID3D11Texture2D>();
                _context.CopyResource(_staging, sourceTex);

                var map = _context.Map(_staging, 0, MapMode.Read, MapFlags.None);
                try
                {
                    var stride = map.RowPitch;
                    unsafe
                    {
                        var ptr = (byte*)map.DataPointer;
                        var span = new ReadOnlySpan<byte>(ptr, stride * Height);
                        var fr = new FrameRef(span, Width, Height, stride, _clock.ElapsedTicks * (10_000_000 / Stopwatch.Frequency));
                        FrameAvailable?.Invoke(fr);
                    }
                }
                finally
                {
                    _context.Unmap(_staging, 0);
                }
            }
            finally
            {
                resource.Dispose();
                _duplication.ReleaseFrame();
            }
        }
    }

    private void ReinitializeQuiet()
    {
        try
        {
            _duplication?.Dispose();
            _duplication = null;
            _staging?.Dispose();
            _staging = null;
            Initialize();
        }
        catch { /* swallow; loop will retry */ Thread.Sleep(200); }
    }

    public void Dispose()
    {
        Stop();
        _staging?.Dispose();
        _duplication?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }
}
