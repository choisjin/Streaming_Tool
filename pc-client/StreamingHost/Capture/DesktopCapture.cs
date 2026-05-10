using System;
using System.Diagnostics;
using System.Threading;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

// MapFlags and ResultCode exist in both Direct3D11 and DXGI namespaces. Disambiguate.
using D3D11MapFlags = Vortice.Direct3D11.MapFlags;
using DXGIResultCode = Vortice.DXGI.ResultCode;

namespace StreamingHost.Capture;

/// <summary>
/// Full-screen capture via DXGI Desktop Duplication. Pulls frames from the
/// chosen monitor (default = primary) into a CPU-readable staging texture and
/// hands the BGRA span to subscribers. Reinitializes on
/// <c>DXGI_ERROR_ACCESS_LOST</c> (resolution change, fullscreen flip, etc.).
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

    public DesktopCapture(int monitorIndex = 0) => _monitorIndex = monitorIndex;

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
        using var factory = CreateDXGIFactory1<IDXGIFactory1>();

        // Adapter 0 (primary GPU)
        if (factory.EnumAdapters1(0, out IDXGIAdapter1? adapter).Failure || adapter is null)
            throw new InvalidOperationException("No DXGI adapter (0) available.");
        using var _adapter = adapter;

        // Create D3D11 device. The Vortice overload returns 3 out params (device, featureLevel, context).
        var hr = D3D11CreateDevice(
            _adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_0 },
            out ID3D11Device tempDevice,
            out _,
            out ID3D11DeviceContext tempContext);
        hr.CheckError();
        _device = tempDevice;
        _context = tempContext;

        // Locate the output (monitor) at the requested index.
        if (_adapter.EnumOutputs((uint)_monitorIndex, out IDXGIOutput? output).Failure || output is null)
            throw new InvalidOperationException($"Monitor {_monitorIndex} not found on adapter 0.");
        using var _output = output;
        using var output1 = _output.QueryInterface<IDXGIOutput1>();

        var desc = _output.Description;
        Width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
        Height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;

        _duplication = output1.DuplicateOutput(_device);

        // CPU-readable staging texture matching capture format.
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        });
    }

    private void Loop()
    {
        if (_duplication is null || _staging is null || _context is null) return;

        while (_running)
        {
            Result hr;
            try
            {
                hr = _duplication.AcquireNextFrame(50, out var _, out IDXGIResource? resource);
                if (hr.Failure)
                {
                    if (hr.Code == DXGIResultCode.WaitTimeout.Code) continue;
                    ReinitializeQuiet();
                    continue;
                }
                using var _resource = resource;
                if (_resource is null) { _duplication.ReleaseFrame(); continue; }

                using var sourceTex = _resource.QueryInterface<ID3D11Texture2D>();
                _context.CopyResource(_staging, sourceTex);

                var map = _context.Map(_staging, 0, MapMode.Read, D3D11MapFlags.None);
                try
                {
                    var stride = (int)map.RowPitch;
                    unsafe
                    {
                        var ptr = (byte*)map.DataPointer;
                        var span = new ReadOnlySpan<byte>(ptr, stride * Height);
                        var fr = new FrameRef(span, Width, Height, stride,
                            _clock.ElapsedTicks * (10_000_000L / Stopwatch.Frequency));
                        FrameAvailable?.Invoke(fr);
                    }
                }
                finally
                {
                    _context.Unmap(_staging, 0);
                    _duplication.ReleaseFrame();
                }
            }
            catch
            {
                ReinitializeQuiet();
            }
        }
    }

    private void ReinitializeQuiet()
    {
        try
        {
            _duplication?.Dispose(); _duplication = null;
            _staging?.Dispose();     _staging = null;
            _context?.Dispose();     _context = null;
            _device?.Dispose();      _device = null;
            Initialize();
        }
        catch
        {
            Thread.Sleep(200);
        }
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
