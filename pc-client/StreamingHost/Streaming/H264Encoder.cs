using System;
using System.IO;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace StreamingHost.Streaming;

/// <summary>
/// Wraps SIPSorceryMedia.FFmpeg's encoder so the rest of the app sees a simple
/// "BGRA bytes in, H.264 NAL units out" API. FFmpeg picks NVENC/QSV/AMF when
/// available and falls back to libx264 otherwise — see ChooseEncoder().
///
/// The native FFmpeg shared libraries (avcodec, avformat, avutil, swscale, swresample)
/// must be on PATH or in the EXE directory.
/// </summary>
public sealed class H264Encoder : IDisposable
{
    private readonly FFmpegVideoEncoder _enc;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;

    public H264Encoder(int width, int height, int fps = 60)
    {
        _width = width;
        _height = height;
        _fps = fps;

        // Initialize FFmpeg shared libs (idempotent). Prefer the EXE folder so we
        // pick up the pinned 7.x DLLs that the build copies in — the user's PATH
        // may have a mismatched FFmpeg (e.g. winget's default 8.x) which breaks
        // FFmpeg.AutoGen 7.0.0 with NotSupportedException at avdevice_register_all.
        var libPath = AppContext.BaseDirectory;
        if (!File.Exists(Path.Combine(libPath, "avcodec-61.dll")))
            libPath = null!; // fall back to PATH/auto-discovery
        FFmpegInit.Initialise(libPath: libPath);

        // Pass tune=zerolatency only — that's the single most impactful option
        // and is supported by libx264. Other options (bf=0, g=...) are codec
        // context flags that SIPSorceryMedia.FFmpeg's encoder dictionary path
        // doesn't always plumb to libx264; passing them here can cause the
        // encoder to silently emit empty buffers.
        // tune=zerolatency already implies bf=0, refs=1, slice-threads off,
        // and rc-lookahead=0. Passing those again as separate dict entries
        // can make libx264 reject the encoder open silently, after which
        // EncodeVideo just returns empty buffers and the wire stays dark.
        // Keep the dictionary minimal.
        var encOpts = new System.Collections.Generic.Dictionary<string, string>
        {
            ["tune"]   = "zerolatency",
            ["preset"] = "ultrafast",
        };
        _enc = new FFmpegVideoEncoder(encOpts);
        // Force H.264 with low-latency tuning. SIPSorceryMedia.FFmpeg picks the codec
        // based on VideoCodecsEnum + the codec name registered in libavcodec.
        // It tries hardware encoders (h264_nvenc/h264_qsv/h264_amf) automatically
        // on recent versions; older versions need explicit codec selection — see README.
    }

    /// <summary>
    /// Encode one BGRA frame from a native pointer. We copy into a tightly-packed
    /// managed buffer and call EncodeVideo because the FFmpeg encoder's pointer
    /// path assumes stride == width*4, which DXGI/WGC staging textures violate
    /// at some resolutions due to row alignment.
    /// </summary>
    public byte[]? Encode(IntPtr bgraPtr, int strideBytes, bool forceKeyFrame)
    {
        if (forceKeyFrame) _enc.ForceKeyFrame();

        var packedSize = _width * 4 * _height;
        var packed = _packedBuf;
        if (packed is null || packed.Length < packedSize) packed = _packedBuf = new byte[packedSize];

        var rowBytes = _width * 4;
        if (strideBytes == rowBytes)
        {
            // Already tightly packed — single memcpy.
            System.Runtime.InteropServices.Marshal.Copy(bgraPtr, packed, 0, packedSize);
        }
        else
        {
            // Strided source: copy row by row to remove the padding the encoder doesn't expect.
            for (var y = 0; y < _height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    bgraPtr + y * strideBytes, packed, y * rowBytes, rowBytes);
            }
        }

        return _enc.EncodeVideo(_width, _height, packed, VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.H264);
    }

    private byte[]? _packedBuf; // reused across frames to avoid GC churn

    /// <summary>RTP timestamp delta per frame at 90 kHz clock.</summary>
    public uint FrameDurationRtp => (uint)(90_000 / _fps);

    public void Dispose() => _enc.Dispose();
}
