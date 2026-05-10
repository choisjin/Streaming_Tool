using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace StreamingHost.Streaming;

/// <summary>
/// One WebRTC peer connection per viewer. Sends a single H.264 video track and
/// receives input commands on a DataChannel.
/// </summary>
public sealed class WebRtcPeer : IDisposable
{
    public readonly string ViewerId;
    private readonly RTCPeerConnection _pc;
    private RTCDataChannel? _inputChannel;

    public event Action<string>? InputMessageReceived;
    public event Action<RTCIceCandidate>? IceCandidateGenerated;

    public WebRtcPeer(string viewerId, IReadOnlyList<RTCIceServer> iceServers)
    {
        ViewerId = viewerId;
        _pc = new RTCPeerConnection(new RTCConfiguration
        {
            iceServers = new List<RTCIceServer>(iceServers),
        });

        // Video track: H.264 baseline. Real frames are pushed via SendVideoFrame().
        var videoFormats = new List<VideoFormat>
        {
            new(VideoCodecsEnum.H264, 96)
        };
        var videoTrack = new MediaStreamTrack(
            new MediaFormatList<VideoFormat>(videoFormats),
            MediaStreamStatusEnum.SendOnly);
        _pc.addTrack(videoTrack);

        _pc.onicecandidate += c => { if (c is not null) IceCandidateGenerated?.Invoke(c); };
        _pc.ondatachannel += dc =>
        {
            if (dc.label == "input")
            {
                _inputChannel = dc;
                dc.onmessage += (_, _, data) =>
                {
                    var text = System.Text.Encoding.UTF8.GetString(data);
                    InputMessageReceived?.Invoke(text);
                };
            }
        };
    }

    public Task<RTCSessionDescriptionInit> CreateOfferAsync()
    {
        var offer = _pc.createOffer();
        return _pc.setLocalDescription(offer).ContinueWith(_ => offer);
    }

    public Task SetRemoteAnswerAsync(string sdp)
    {
        var answer = new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdp };
        return Task.FromResult(_pc.setRemoteDescription(answer));
    }

    public void AddRemoteIce(RTCIceCandidateInit cand)
    {
        _pc.addIceCandidate(cand);
    }

    /// <summary>
    /// Push an encoded H.264 access unit (Annex-B or RBSP) to the viewer.
    /// Encoder is responsible for producing correct timestamps.
    /// </summary>
    public void SendEncodedH264(byte[] nalu, uint durationRtpUnits)
    {
        _pc.SendVideo(durationRtpUnits, nalu);
    }

    public void Dispose()
    {
        try { _pc.close(); } catch { }
    }
}
