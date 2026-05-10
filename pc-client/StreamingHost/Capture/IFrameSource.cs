using System;

namespace StreamingHost.Capture;

/// <summary>
/// Source of raw video frames. Implementations: full desktop (DXGI), per-window (WGC).
/// </summary>
public interface IFrameSource : IDisposable
{
    int Width { get; }
    int Height { get; }
    /// <summary>Subscribe to frames. Frame buffer is BGRA8, valid only during the callback.</summary>
    event FrameAvailableHandler? FrameAvailable;
    void Start();
    void Stop();
}

public delegate void FrameAvailableHandler(FrameRef frame);

public readonly ref struct FrameRef
{
    public readonly ReadOnlySpan<byte> Bgra;
    /// <summary>Native pointer to the same BGRA buffer; valid only during the callback.</summary>
    public readonly IntPtr BgraPtr;
    public readonly int Width;
    public readonly int Height;
    public readonly int Stride;
    public readonly long TimestampHns; // 100ns ticks since process start

    public FrameRef(ReadOnlySpan<byte> bgra, IntPtr bgraPtr, int width, int height, int stride, long timestampHns)
    {
        Bgra = bgra;
        BgraPtr = bgraPtr;
        Width = width;
        Height = height;
        Stride = stride;
        TimestampHns = timestampHns;
    }
}
