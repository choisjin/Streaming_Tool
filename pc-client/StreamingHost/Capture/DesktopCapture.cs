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

    /// <summary>Adapter+output that DuplicateOutput finally accepted (for diagnostics).</summary>
    public string PickedAdapterDescription { get; private set; } = "";
    public string PickedOutputDescription { get; private set; } = "";
    /// <summary>One line per (adapter, output) considered, with the resulting HRESULT.</summary>
    public System.Collections.Generic.IReadOnlyList<string> EnumerationLog { get; private set; } =
        System.Array.Empty<string>();

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

        // Walk every (adapter, output) combination and pick the first pair where
        // DuplicateOutput actually succeeds. Microsoft Basic Display Adapter,
        // virtual display drivers, and orphaned RDP devices all surface as
        // adapters/outputs that EnumOutputs returns happily but DuplicateOutput
        // rejects with E_INVALIDARG (0x80070057) or DXGI_ERROR_UNSUPPORTED (0x887A0004).
        var attempts = new System.Collections.Generic.List<string>();

        for (uint a = 0; ; a++)
        {
            if (factory.EnumAdapters1(a, out IDXGIAdapter1? adapter).Failure || adapter is null) break;
            var aDesc = adapter.Description1.Description;
            attempts.Add($"adapter[{a}] = \"{aDesc}\"");

            var hadOutput = false;
            for (uint o = 0; ; o++)
            {
                if (adapter.EnumOutputs(o, out IDXGIOutput? output).Failure || output is null) break;
                hadOutput = true;
                var oDesc = output.Description.DeviceName;

                ID3D11Device? tempDevice = null;
                ID3D11DeviceContext? tempContext = null;
                try
                {
                    var hr = D3D11CreateDevice(
                        adapter,
                        DriverType.Unknown,
                        DeviceCreationFlags.BgraSupport,
                        new[] { FeatureLevel.Level_11_0 },
                        out tempDevice,
                        out _,
                        out tempContext);
                    if (hr.Failure)
                    {
                        attempts.Add($"  [{a}.{o}] {aDesc} / {oDesc}: D3D11CreateDevice failed 0x{hr.Code:X8}");
                        continue;
                    }

                    using var output1 = output.QueryInterface<IDXGIOutput1>();
                    var dup = output1.DuplicateOutput(tempDevice);

                    // Success — keep this pair.
                    var desc = output.Description;
                    Width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
                    Height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;
                    _device = tempDevice; tempDevice = null;
                    _context = tempContext; tempContext = null;
                    _duplication = dup;
                    PickedAdapterDescription = aDesc;
                    PickedOutputDescription = oDesc;

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

                    output.Dispose();
                    adapter.Dispose();
                    return;
                }
                catch (SharpGen.Runtime.SharpGenException sx)
                {
                    attempts.Add($"  [{a}.{o}] {aDesc} / {oDesc}: DuplicateOutput 0x{sx.HResult:X8}");
                }
                catch (Exception ex)
                {
                    attempts.Add($"  [{a}.{o}] {aDesc} / {oDesc}: {ex.GetType().Name} {ex.Message}");
                }
                finally
                {
                    tempContext?.Dispose();
                    tempDevice?.Dispose();
                    output.Dispose();
                }
            }

            if (!hadOutput) attempts.Add($"  [{a}.*] (no outputs)");
            adapter.Dispose();
        }

        EnumerationLog = attempts;
        var detail = attempts.Count == 0 ? "no adapters/outputs were enumerated" : string.Join("\n", attempts);
        throw new InvalidOperationException(
            "Desktop Duplication is unsupported on every adapter/output pair. " +
            "This usually means the session has no real GPU output (RDP, " +
            "Hyper-V Enhanced Session, headless server, Microsoft Basic Display Adapter), " +
            "or the GPU driver is too old. Tried:\n" + detail);
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
