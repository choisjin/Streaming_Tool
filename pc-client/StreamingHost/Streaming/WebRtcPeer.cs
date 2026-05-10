using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using StreamingHost.Signaling;

namespace StreamingHost.Streaming;

/// <summary>
/// One WebRTC peer per viewer. Wraps an SIPSorcery <see cref="RTCPeerConnection"/>,
/// drives the offer/answer + ICE handshake against a <see cref="ViewerSession"/>
/// from the embedded signaling server, sends an H.264 video track, and surfaces
/// JSON input messages received on a "input" DataChannel.
/// </summary>
public sealed class WebRtcPeer : IDisposable
{
    public string ViewerId => _session.ViewerId;
    public RTCPeerConnectionState State => _pc.connectionState;

    private readonly ViewerSession _session;
    private readonly RTCPeerConnection _pc;
    private readonly MediaStreamTrack _videoTrack;
    private RTCDataChannel? _inputChannel;

    /// <summary>JSON text from the "input" DataChannel.</summary>
    public event Action<string>? InputMessageReceived;

    public event Action<RTCPeerConnectionState>? ConnectionStateChanged;

    public WebRtcPeer(ViewerSession session, IReadOnlyList<RTCIceServer>? iceServers = null)
    {
        _session = session;

        _pc = new RTCPeerConnection(new RTCConfiguration
        {
            iceServers = new List<RTCIceServer>(iceServers ?? Array.Empty<RTCIceServer>()),
        });

        // Offer H.264 only — broadly supported by mobile browsers and easy for hardware encoders.
        var h264 = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "H264", 90000,
            "packetization-mode=1;profile-level-id=42e01f");

        _videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video,
            isRemote: false,
            new List<SDPAudioVideoMediaFormat> { h264 },
            MediaStreamStatusEnum.SendOnly);
        _pc.addTrack(_videoTrack);

        _pc.onicecandidate += OnLocalIceCandidate;
        _pc.onconnectionstatechange += s => ConnectionStateChanged?.Invoke(s);
        _pc.ondatachannel += OnRemoteDataChannel;

        _session.Message += OnSignalingMessage;
    }

    /// <summary>Create local offer and send via signaling.</summary>
    public async Task StartOfferAsync()
    {
        var offer = _pc.createOffer();
        await _pc.setLocalDescription(offer);
        await _session.SendOfferAsync(offer.sdp);
    }

    /// <summary>Push an encoded H.264 access unit (Annex-B byte stream).</summary>
    public void SendH264Frame(byte[] nalu, uint durationRtpUnits)
    {
        if (_pc.connectionState != RTCPeerConnectionState.connected) return;
        _pc.SendVideo(durationRtpUnits, nalu);
    }

    public void Dispose()
    {
        _session.Message -= OnSignalingMessage;
        try { _pc.close(); } catch { }
    }

    // --- signaling glue ---

    private void OnSignalingMessage(string type, JsonNode msg)
    {
        if (type == "answer")
        {
            var sdp = msg["sdp"]?.GetValue<string>();
            if (sdp is null) return;
            _ = _pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdp });
        }
        else if (type == "ice")
        {
            var cand = msg["candidate"];
            if (cand is null) return;
            try
            {
                var init = new RTCIceCandidateInit
                {
                    candidate = cand["candidate"]?.GetValue<string>() ?? "",
                    sdpMid = cand["sdpMid"]?.GetValue<string>(),
                    sdpMLineIndex = (ushort?)cand["sdpMLineIndex"]?.GetValue<int>() ?? 0,
                };
                if (!string.IsNullOrEmpty(init.candidate))
                    _pc.addIceCandidate(init);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[webrtc] addIceCandidate failed: {ex.Message}");
            }
        }
    }

    private void OnLocalIceCandidate(RTCIceCandidate cand)
    {
        if (cand is null) return;
        var json = new
        {
            candidate = cand.candidate,
            sdpMid = cand.sdpMid,
            sdpMLineIndex = cand.sdpMLineIndex,
        };
        _ = _session.SendIceAsync(json);
    }

    private void OnRemoteDataChannel(RTCDataChannel dc)
    {
        if (dc.label != "input") return;
        _inputChannel = dc;
        dc.onmessage += (_, _, data) =>
        {
            try
            {
                var text = System.Text.Encoding.UTF8.GetString(data);
                InputMessageReceived?.Invoke(text);
            }
            catch { /* ignore malformed */ }
        };
    }
}
