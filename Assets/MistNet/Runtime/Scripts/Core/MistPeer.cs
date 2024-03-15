using Cysharp.Threading.Tasks;
using System;
using Unity.WebRTC;

namespace MistNet
{
    /// <summary>
    /// TODO: 相手のPeerでDataChannelが開いていない？
    /// </summary>
    public class MistPeer
    {
        private static readonly float WaitReconnectTimeSec = 3f;
        private static readonly string DataChannelLabel = "data";

        public readonly RTCPeerConnection Connection;
        public MistSignalingState SignalingState;

        public string Id;
        private RTCDataChannel _dataChannel;
        // private readonly MediaStream _remoteStream = new();

        public readonly Action<byte[], string> OnMessage;
        public Action<Ice> OnCandidate;
        public readonly Action<string> OnConnected;
        public readonly Action<string> OnDisconnected;

        public MistPeer(string id)
        {
            Id = id;
            OnMessage += MistManager.I.OnMessage;
            OnConnected += MistManager.I.OnConnected;
            OnDisconnected += MistManager.I.OnDisconnected;

            // ----------------------------
            // Configuration
            var configuration = default(RTCConfiguration);
            configuration.iceServers = new RTCIceServer[]
            {
                new() { urls = MistConfig.StunUrls }
            };
            Connection = new RTCPeerConnection(ref configuration);

            // ----------------------------
            // Candidate
            Connection.OnIceCandidate += OnIceCandidate;
            Connection.OnIceConnectionChange += OnIceConnectionChange;
            Connection.OnIceGatheringStateChange += state => MistDebug.Log($"[Signaling][OnIceGatheringStateChange] {state}");
            Connection.OnNegotiationNeeded += () => MistDebug.Log($"[Signaling][OnNegotiationNeeded] -> {Id}");
            Connection.OnTrack = e => MistDebug.Log($"[Signaling][OnTrack] -> {Id}");
            // ----------------------------
            // DataChannels
            SetDataChannel();

            // ----------------------------
            // SignalingState
            SignalingState = MistSignalingState.InitialStable;
        }

        public async UniTask<RTCSessionDescription> CreateOffer()
        {
            MistDebug.Log($"[Signaling][CreateOffer] -> {Id}");

            CreateDataChannel(); // DataChannelを作成

            // ----------------------------
            // CreateOffer
            var offerOperation = Connection.CreateOffer();
            await offerOperation;
            if (offerOperation.IsError)
            {
                MistDebug.LogError($"[Signaling][{Id}][Error][OfferOperation]");
                MistPeerData.I.SetState(Id, MistPeerState.Disconnected);
                SignalingState = MistSignalingState.InitialStable;
                return default;
            }

            // ----------------------------
            // LocalDescription
            var desc = offerOperation.Desc;
            var localDescriptionOperation = Connection.SetLocalDescription(ref desc);
            if (localDescriptionOperation.IsError)
            {
                MistDebug.LogError($"[Signaling][{Id}][Error][SetLocalDescription]");
                MistPeerData.I.SetState(Id, MistPeerState.Disconnected);
                SignalingState = MistSignalingState.InitialStable;
                return default;
            }

            SignalingState = MistSignalingState.NegotiationInProgress;
            return desc;
        }

        public async UniTask<RTCSessionDescription> CreateAnswer(RTCSessionDescription remoteDescription)
        {
            MistDebug.Log($"[Signaling][CreateAnswer] -> {Id}");

            // GM.Msg("SetTrack", Connection); // 音声等を追加する
            // Connection.OnTrack = e =>
            // {
            //     if (e.Track.Kind == TrackKind.Audio)
            //     {
            //         _remoteStream.AddTrack(e.Track);
            //     }
            // };

            // ----------------------------
            // RemoteDescription
            var remoteDescriptionOperation = Connection.SetRemoteDescription(ref remoteDescription);
            await remoteDescriptionOperation;
            if (remoteDescriptionOperation.IsError)
            {
                MistPeerData.I.SetState(Id, MistPeerState.Disconnected);
                Reconnect().Forget();
                MistDebug.LogError(
                    $"[Signaling][Error][SetRemoteDescription] -> {Id} {remoteDescriptionOperation.Error.message}");
                SignalingState = MistSignalingState.InitialStable;
                return default;
            }

            // ----------------------------
            // CreateAnswer
            var answerOperation = Connection.CreateAnswer();
            await answerOperation;
            if (answerOperation.IsError)
            {
                MistPeerData.I.SetState(Id, MistPeerState.Disconnected);
                Reconnect().Forget();
                MistDebug.LogError($"[Signaling][Error][CreateAnswer] -> {Id} {answerOperation.Error.message}");
                SignalingState = MistSignalingState.InitialStable;
                return default;
            }

            // ----------------------------
            // LocalDescription
            var desc = answerOperation.Desc;
            var localDescriptionOperation = Connection.SetLocalDescription(ref desc);
            if (localDescriptionOperation.IsError)
            {
                MistPeerData.I.SetState(Id, MistPeerState.Disconnected);
                Reconnect().Forget();
                MistDebug.LogError(
                    $"[Signaling][Error][SetLocalDescription] -> {Id} {localDescriptionOperation.Error.message}");
                SignalingState = MistSignalingState.InitialStable;
                return default;
            }

            SignalingState = MistSignalingState.NegotiationInProgress;
            return desc;
        }

        private void CreateDataChannel()
        {
            var config = new RTCDataChannelInit();
            _dataChannel = Connection.CreateDataChannel(DataChannelLabel, config);
            _dataChannel.OnMessage = OnMessageDataChannel;
            _dataChannel.OnOpen = OnOpenDataChannel;
            _dataChannel.OnClose = OnCloseDataChannel;
        }

        private void SetDataChannel()
        {
            MistDebug.Log($"[Signaling][SetDataChannel] -> {Id}");
            Connection.OnDataChannel = channel =>
            {
                MistDebug.Log("OnDataChannel");
                _dataChannel = channel;
                _dataChannel.OnMessage = OnMessageDataChannel;
                _dataChannel.OnOpen = OnOpenDataChannel;
                _dataChannel.OnClose = OnCloseDataChannel;
                OnOpenDataChannel();
            };
        }

        public async UniTaskVoid SetRemoteDescription(RTCSessionDescription remoteDescription)
        {
            MistDebug.Log($"[Signaling][SetRemoteDescription] -> {Id}");

            var remoteDescriptionOperation = Connection.SetRemoteDescription(ref remoteDescription);
            await remoteDescriptionOperation;
            if (remoteDescriptionOperation.IsError)
            {
                MistDebug.LogError(
                    $"[Signaling][Error][SetRemoteDescription] -> {Id} {remoteDescriptionOperation.Error.message}");
                MistPeerData.I.SetState(Id, MistPeerState.Disconnected);
                SignalingState = MistSignalingState.InitialStable;
            }
        }

        public void AddIceCandidate(Ice candidate)
        {
            Connection.AddIceCandidate(candidate.Get());
        }

        public async UniTaskVoid Send(byte[] data)
        {
            if (MistConfig.LatencyMilliseconds > 0)
            {
                await UniTask.Delay(MistConfig.LatencyMilliseconds);
            }

            if (SignalingState == MistSignalingState.NegotiationInProgress)
            {
                await UniTask.WaitUntil(() => _dataChannel is { ReadyState: RTCDataChannelState.Open });
            }
            
            switch (_dataChannel)
            {
                case { ReadyState: RTCDataChannelState.Closed }:
                case { ReadyState: RTCDataChannelState.Closing }:
                    MistDebug.LogWarning($"[Signaling][Send] DataChannel is closed -> {Id}");
                    return;
            }
            
            // _dataChannelがCloseのとき
            if (_dataChannel == null)
            {
                MistDebug.LogWarning($"[Signaling][Send] DataChannel is null -> {Id}");
                return;
            }

            // 評価用
            if (MistStats.I != null)
            {
                MistStats.I.TotalSendBytes += data.Length;
                MistStats.I.TotalMessengeCount++;
            }
            
            _dataChannel.Send(data);
        }

        public void Close()
        {
            // DataChannelを閉じる
            _dataChannel?.Close();

            // PeerConnectionを閉じる
            Connection.Close();

            SignalingState = MistSignalingState.InitialStable;
        }

        public void ForceClose()
        {
            // DataChannelを閉じる
            _dataChannel?.Close();

            // PeerConnectionを閉じる
            Connection.Close();

            SignalingState = MistSignalingState.InitialStable;
        }

        private void OnIceConnectionChange(RTCIceConnectionState state)
        {
            MistDebug.Log($"[Signaling][OnIceConnectionChange] {state} -> {Id}");

            switch (state)
            {
                case RTCIceConnectionState.Connected:
                    SignalingState = MistSignalingState.NegotiationCompleted;
                    OnConnected?.Invoke(Id);
                    break;
                case RTCIceConnectionState.Closed:
                case RTCIceConnectionState.Disconnected:
                case RTCIceConnectionState.Failed:
                case RTCIceConnectionState.Max:
                    Connection.Close();
                    SignalingState = MistSignalingState.InitialStable;
                    OnDisconnected?.Invoke(Id);
                    break;
                case RTCIceConnectionState.New:
                    break;
                case RTCIceConnectionState.Checking:
                    break;
                case RTCIceConnectionState.Completed:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        private void OnOpenDataChannel()
        {
            MistDebug.Log($"[Signaling][DataChannel] Open -> {Id}");
            OnIceConnectionChange(RTCIceConnectionState.Connected); // NOTE: このメソッドが呼ばれないため、ここで強制的に呼ぶ
        }

        private void OnCloseDataChannel()
        {
            MistDebug.Log($"[Signaling][DataChannel] Finalize -> {Id}");
            SignalingState = MistSignalingState.InitialStable;
            OnIceConnectionChange(RTCIceConnectionState.Disconnected);　// NOTE: このメソッドが呼ばれないため、ここで強制的に呼ぶ
        }

        private void OnMessageDataChannel(byte[] data)
        {
            if (MistStats.I != null) MistStats.I.TotalReceiveBytes += data.Length;
            OnMessage?.Invoke(data, Id);
        }

        private void OnIceCandidate(RTCIceCandidate candidate)
        {
            MistDebug.Log($"[Signaling][OnIceCandidate] -> {Id}");
            OnCandidate?.Invoke(new Ice(candidate));
        }

        private async UniTask Reconnect()
        {
            await UniTask.Delay(TimeSpan.FromSeconds(WaitReconnectTimeSec));
            CreateOffer().Forget();
            MistPeerData.I.SetState(Id, MistPeerState.Connecting);
        }
    }

    [Serializable]
    public class Ice
    {
        public string Candidate;
        public string SdpMid;
        public int SdpMLineIndex;

        public Ice(RTCIceCandidate candidate)
        {
            Candidate = candidate.Candidate;
            SdpMid = candidate.SdpMid;
            SdpMLineIndex = (int)candidate.SdpMLineIndex;
        }

        public RTCIceCandidate Get()
        {
            var data = new RTCIceCandidate(new RTCIceCandidateInit
            {
                candidate = Candidate,
                sdpMid = SdpMid,
                sdpMLineIndex = SdpMLineIndex
            });
            return data;
        }
    }
    
    public enum MistSignalingState
    {
        InitialStable, NegotiationInProgress, NegotiationCompleted
    }
}