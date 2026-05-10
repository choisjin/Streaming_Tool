using System;
using System.Collections.Concurrent;
using System.Threading;
using StreamingHost.Capture;

namespace StreamingHost.Streaming;

/// <summary>
/// One capture source -> one encoder -> fan-out to N peers. Encoder runs on the
/// capture thread (single-threaded encode is fine for one viewer; multi-viewer
/// reuses the same encoded NAL units so cost is constant in viewer count).
/// </summary>
public sealed class StreamingPipeline : IDisposable
{
    private readonly IFrameSource _source;
    private readonly H264Encoder _encoder;
    private readonly ConcurrentDictionary<string, WebRtcPeer> _peers = new();
    private long _frameCount;
    private long _lastKeyframeMs;

    /// <summary>Force a keyframe at most this often when no peer requested one.</summary>
    private const int KEYFRAME_INTERVAL_MS = 2000;

    public StreamingPipeline(IFrameSource source, int fps = 60)
    {
        _source = source;
        _encoder = new H264Encoder(source.Width, source.Height, fps);
        _source.FrameAvailable += OnFrame;
    }

    public void Start() => _source.Start();
    public void Stop() => _source.Stop();

    public void AddPeer(WebRtcPeer peer)  => _peers[peer.ViewerId] = peer;
    public void RemovePeer(string viewerId) => _peers.TryRemove(viewerId, out _);

    private void OnFrame(FrameRef frame)
    {
        if (_peers.IsEmpty) return; // no encoders if no viewers — saves CPU/GPU

        var nowMs = Environment.TickCount64;
        var forceKey = nowMs - _lastKeyframeMs > KEYFRAME_INTERVAL_MS || Interlocked.Read(ref _frameCount) == 0;
        if (forceKey) _lastKeyframeMs = nowMs;

        var nalu = _encoder.Encode(frame.Bgra, frame.Stride, forceKey);
        Interlocked.Increment(ref _frameCount);
        if (nalu is null || nalu.Length == 0) return;

        var dur = _encoder.FrameDurationRtp;
        foreach (var peer in _peers.Values)
        {
            try { peer.SendH264Frame(nalu, dur); }
            catch { /* one bad peer shouldn't kill the loop */ }
        }
    }

    public void Dispose()
    {
        _source.FrameAvailable -= OnFrame;
        _source.Stop();
        _encoder.Dispose();
        foreach (var p in _peers.Values) p.Dispose();
        _peers.Clear();
    }
}
