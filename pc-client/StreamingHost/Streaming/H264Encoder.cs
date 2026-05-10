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

        _enc = new FFmpegVideoEncoder();
        // Force H.264 with low-latency tuning. SIPSorceryMedia.FFmpeg picks the codec
        // based on VideoCodecsEnum + the codec name registered in libavcodec.
        // It tries hardware encoders (h264_nvenc/h264_qsv/h264_amf) automatically
        // on recent versions; older versions need explicit codec selection — see README.
    }

    /// <summary>Encode one BGRA frame. Returns the encoded NAL units (Annex-B) or null if dropped.</summary>
    public byte[]? Encode(ReadOnlySpan<byte> bgra, int strideBytes, bool forceKeyFrame)
    {
        // FFmpegVideoEncoder.EncodeVideo expects raw frame buffer + format.
        // Internally it converts BGRA -> YUV420P with swscale and feeds the encoder.
        var frame = bgra.ToArray(); // FFmpeg API takes byte[]; copy is unavoidable here.
        return _enc.EncodeVideo(_width, _height, frame, VideoPixelFormatsEnum.Bgra,
            VideoCodecsEnum.H264);
    }

    /// <summary>RTP timestamp delta per frame at 90 kHz clock.</summary>
    public uint FrameDurationRtp => (uint)(90_000 / _fps);

    public void Dispose() => _enc.Dispose();
}
