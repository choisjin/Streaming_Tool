using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using static Vortice.Direct3D11.D3D11;

// Disambiguate: both Direct3D11 and DXGI define MapFlags.
using D3D11MapFlags = Vortice.Direct3D11.MapFlags;

namespace StreamingHost.Capture;

/// <summary>
/// Screen capture via Windows.Graphics.Capture (WGC). Works on hybrid GPU
/// laptops, RDP sessions, and virtual displays where DXGI Desktop Duplication
/// fails with E_INVALIDARG / DXGI_ERROR_UNSUPPORTED.
///
/// Currently captures the primary monitor as a single source. Phase 3 will
/// reuse this class with GraphicsCaptureItem.CreateFromWindowId for per-window.
/// </summary>
public sealed class WindowsGraphicsCapture : IFrameSource
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private ID3D11Texture2D? _staging;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;

    private readonly Stopwatch _clock = new();
    private volatile bool _running;
    private int _stagingWidth, _stagingHeight; // tracks current staging size for dynamic resolution

    public int Width { get; private set; }
    public int Height { get; private set; }
    public string PickedSourceDescription { get; private set; } = "";
    public event FrameAvailableHandler? FrameAvailable;

    public static bool IsAvailable() => GraphicsCaptureSession.IsSupported();

    public void Start()
    {
        if (!IsAvailable())
            throw new InvalidOperationException("Windows.Graphics.Capture is not supported on this system.");

        // 1) Pick the primary monitor and ask WGC for a capture item from its HMONITOR.
        var hmon = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
        if (hmon == IntPtr.Zero) throw new InvalidOperationException("No primary monitor.");

        // GraphicsCaptureItem can only be made from win32 handles via
        // IGraphicsCaptureItemInterop. CsWinRT gets us the activation factory
        // (IInspectable). We then QueryInterface that IInspectable to the
        // private IGraphicsCaptureItemInterop and invoke its vtable directly,
        // sidestepping any RCW that might hide the interop interface.
        var factoryRef = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interopIid = IID_IGraphicsCaptureItemInterop;
        IntPtr interopPtr;
        var qiHr = Marshal.QueryInterface(factoryRef.ThisPtr, ref interopIid, out interopPtr);
        if (qiHr != 0)
            throw new InvalidOperationException(
                $"GraphicsCaptureItem activation factory does not expose IGraphicsCaptureItemInterop (QI HRESULT 0x{qiHr:X8}). " +
                "This typically means the WinRT apartment is not initialized properly or the system lacks Windows.Graphics.Capture.");

        IntPtr itemRaw;
        try
        {
            // IGraphicsCaptureItem IID per Windows.Graphics.Capture metadata.
            // Using typeof(GraphicsCaptureItem).GUID is unreliable: CsWinRT's
            // projected type doesn't always match the runtime interface IID.
            var iid = IID_IGraphicsCaptureItem;
            unsafe
            {
                // IGraphicsCaptureItemInterop vtable (after IUnknown's 3 slots):
                //   slot 3: HRESULT CreateForWindow(HWND, REFIID, void**)
                //   slot 4: HRESULT CreateForMonitor(HMONITOR, REFIID, void**)
                var vtbl = *(IntPtr**)interopPtr;
                var createForMonitor = (delegate* unmanaged<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtbl[4];
                IntPtr outItem;
                int hr = createForMonitor(interopPtr, hmon, &iid, &outItem);
                if (hr != 0)
                    throw new InvalidOperationException(
                        $"IGraphicsCaptureItemInterop.CreateForMonitor returned 0x{hr:X8}. " +
                        $"hmon=0x{hmon.ToInt64():X}, IID_IGraphicsCaptureItem={iid}");
                itemRaw = outItem;
            }
        }
        finally
        {
            Marshal.Release(interopPtr);
        }

        _item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemRaw);
        Marshal.Release(itemRaw);

        Width  = _stagingWidth  = _item.Size.Width;
        Height = _stagingHeight = _item.Size.Height;
        PickedSourceDescription = _item.DisplayName;

        // 2) Create a D3D11 device. Picking adapter 0 is fine for WGC since the
        //    OS compositor blits frames into our textures regardless of which
        //    GPU rendered them.
        var hrCreate = D3D11CreateDevice(
            null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_0 },
            out ID3D11Device dev, out _, out ID3D11DeviceContext ctx);
        hrCreate.CheckError();
        _device = dev; _context = ctx;

        // 3) Build the WGC frame pool tied to a WinRT device wrapper around _device.
        var winrtDevice = Direct3D11Interop.CreateDirect3DDevice(_device);
        _pool = Direct3D11CaptureFramePool.Create(
            winrtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            size: _item.Size);
        _pool.FrameArrived += OnFrameArrived;

        _session = _pool.CreateCaptureSession(_item);
        // Disable cursor + border in capture (cursor stays on the user's screen anyway).
        _session.IsCursorCaptureEnabled = true;
        _session.IsBorderRequired = false;

        _staging = CreateStaging(_device, Width, Height);

        _clock.Start();
        _running = true;
        _session.StartCapture();
    }

    public void Stop()
    {
        _running = false;
        _session?.Dispose();    _session = null;
        _pool?.Dispose();       _pool = null;
        _item = null;           // GraphicsCaptureItem doesn't implement IDisposable
        _staging?.Dispose();    _staging = null;
        _context?.Dispose();    _context = null;
        _device?.Dispose();     _device = null;
    }

    public void Dispose() => Stop();

    private static ID3D11Texture2D CreateStaging(ID3D11Device device, int w, int h) =>
        device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w, Height = (uint)h,
            MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        });

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object _)
    {
        if (!_running || _device is null || _context is null) return;

        using var frame = sender.TryGetNextFrame();
        if (frame is null) return;

        var size = frame.ContentSize;
        if (size.Width == 0 || size.Height == 0) return;

        // If the source resized (e.g. resolution change, monitor switch), recreate the staging
        // texture and the pool to match.
        if (size.Width != _stagingWidth || size.Height != _stagingHeight)
        {
            _stagingWidth = size.Width; _stagingHeight = size.Height;
            Width = size.Width; Height = size.Height;
            _staging?.Dispose();
            _staging = CreateStaging(_device, Width, Height);
            sender.Recreate(
                Direct3D11Interop.CreateDirect3DDevice(_device),
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                numberOfBuffers: 2,
                size: size);
            return; // skip this frame; the next FrameArrived will land in the new pool
        }

        using var sourceTex = Direct3D11Interop.GetTexture2D(frame.Surface);
        _context.CopyResource(_staging!, sourceTex);

        var map = _context.Map(_staging!, 0, MapMode.Read, D3D11MapFlags.None);
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
            _context.Unmap(_staging!, 0);
        }
    }

    // --- P/Invoke for the primary monitor ---

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    /// <summary>IID of IGraphicsCaptureItemInterop. Vtable slots 3/4 = CreateForWindow / CreateForMonitor.</summary>
    private static readonly Guid IID_IGraphicsCaptureItemInterop =
        new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    /// <summary>IID of IGraphicsCaptureItem (the WinRT default interface of GraphicsCaptureItem).</summary>
    private static readonly Guid IID_IGraphicsCaptureItem =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
}
