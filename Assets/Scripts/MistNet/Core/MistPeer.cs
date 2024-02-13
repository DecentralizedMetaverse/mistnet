using Cysharp.Threading.Tasks;
using System;
using Unity.WebRTC;
using UnityEngine;

namespace MistNet
{
    /// <summary>
    /// TODO: 相手のPeerでDataChannelが開いていない？
    /// </summary>
    public class MistPeer
    {
        private static readonly float WaitReconnectTimeSec = 3f;
        private static readonly string DataChannelLabel = "data";

        private static readonly string[] StunUrls =
        {
            "stun:stun.l.google.com:19302",
            "stun:stun1.l.google.com:19302",
            "stun:stun2.l.google.com:19302",
            "stun:stun3.l.google.com:19302",
            "stun:stun4.l.google.com:19302"
        };

        public RTCPeerConnection Connection;

        public string Id;
        private RTCDataChannel _dataChannel;
        private MediaStream _remoteStream = new();

        public Action<byte[], string> OnMessage;
        public Action<Ice> OnCandidate;
        public Action<string> OnConnected, OnDisconnected;

        public MistPeer(string id)
        {
            this.Id = id;
            OnMessage += MistManager.I.OnMessage;
            OnConnected += MistManager.I.OnConnected;
            OnDisconnected += MistManager.I.OnDisconnected;

            // ----------------------------
            // Configuration
            var configuration = default(RTCConfiguration);
            configuration.iceServers = new RTCIceServer[]
            {
                new RTCIceServer { urls = StunUrls }
            };
            Connection = new RTCPeerConnection(ref configuration);

            // ----------------------------
            // Candidate
            Connection.OnIceCandidate = OnIceCandidate;
            Connection.OnIceConnectionChange = OnIceConnectionChange;

            // ----------------------------
            // DataChannels
            SetDataChannel();
        }

        public async UniTask<RTCSessionDescription> CreateOffer()
        {
            Debug.Log($"[Signaling][CreateOffer] -> {Id}");

            CreateDataChannel(); // DataChannelを作成

            // ----------------------------
            // CreateOffer
            var offerOperation = Connection.CreateOffer();
            await offerOperation;
            if (offerOperation.IsError)
            {
                Debug.LogError($"[Signaling][{Id}][Error][OfferOperation]");
                MistPeerData.I.SetState(Id, MistPeerState.Disconnected);
            }

            // ----------------------------
            // LocalDescription
            var desc = offerOperation.Desc;
            var localDescriptionOperation = Connection.SetLocalDescription(ref desc);
            if (localDescriptionOperation.IsError)
            {
                Debug.LogError($"[Signaling][{Id}][Error][SetLocalDescription]");
                MistPeerData.I.SetState(Id, MistPeerState.Disconnected);
            }

            return desc;
        }

        public async UniTask<RTCSessionDescription> CreateAnswer(RTCSessionDescription remoteDescription)
        {
            Debug.Log($"[Signaling][CreateAnswer] -> {Id}");

            // GM.Msg("SetTrack", Connection); // 音声等を追加する
            Connection.OnTrack = (RTCTrackEvent e) =>
            {
                if (e.Track.Kind == TrackKind.Audio)
                {
                    _remoteStream.AddTrack(e.Track);
                }
            };

            // ----------------------------
            // RemoteDescription
            var remoteDescriptionOperation = Connection.SetRemoteDescription(ref remoteDescription);
            await remoteDescriptionOperation;
            if (remoteDescriptionOperation.IsError)
            {
                MistPeerData.I.SetState(Id, MistPeerState.Disconnected);
                Reconnect().Forget();
                Debug.LogError(
                    $"[Signaling][Error][SetRemoteDescription] -> {Id} {remoteDescriptionOperation.Error.message}");
            }

            // ----------------------------
            // CreateAnswer
            var answerOperation = Connection.CreateAnswer();
            await answerOperation;
            if (answerOperation.IsError)
            {
                MistPeerData.I.SetState(Id, MistPeerState.Disconnected);
                Reconnect().Forget();
                Debug.LogError($"[Signaling][Error][CreateAnswer] -> {Id} {answerOperation.Error.message}");
            }

            // ----------------------------
            // LocalDescription
            var desc = answerOperation.Desc;
            var localDescriptionOperation = Connection.SetLocalDescription(ref desc);
            if (localDescriptionOperation.IsError)
            {
                MistPeerData.I.SetState(Id, MistPeerState.Disconnected);
                Reconnect().Forget();
                Debug.LogError(
                    $"[Signaling][Error][SetLocalDescription] -> {Id} {localDescriptionOperation.Error.message}");
            }

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
            Debug.Log($"[Signaling][SetDataChannel] -> {Id}");
            Connection.OnDataChannel = channel =>
            {
                Debug.Log("OnDataChannel");
                _dataChannel = channel;
                _dataChannel.OnMessage = OnMessageDataChannel;
                _dataChannel.OnOpen = OnOpenDataChannel;
                _dataChannel.OnClose = OnCloseDataChannel;
                OnOpenDataChannel();
            };
        }

        public async UniTaskVoid SetRemoteDescription(RTCSessionDescription remoteDescription)
        {
            Debug.Log($"[Signaling][SetRemoteDescription] -> {Id}");

            var remoteDescriptionOperation = Connection.SetRemoteDescription(ref remoteDescription);
            await remoteDescriptionOperation;
            if (remoteDescriptionOperation.IsError)
            {
                Debug.LogError(
                    $"[Signaling][Error][SetRemoteDescription] -> {Id} {remoteDescriptionOperation.Error.message}");
                MistPeerData.I.SetState(Id, MistPeerState.Disconnected);
            }
        }

        public void AddIceCandidate(Ice candidate)
        {
            Connection.AddIceCandidate(candidate.Get());
        }

        public void Send(string data)
        {
            if (_dataChannel == null)
            {
                // WebRTC交換時において、まだ接続が確立していないときがある
                Debug.LogWarning("dataChannel is null");
                return;
            }
            else if (_dataChannel.ReadyState != RTCDataChannelState.Open)
            {
                Debug.LogWarning("dataChannel is not open");
                return;
            }
            
            MistStats.I.TotalSendBytes += data.Length;
            _dataChannel.Send(data);
        }

        public async UniTaskVoid Send(byte[] data)
        {
            await UniTask.WaitUntil(() => _dataChannel != null && _dataChannel.ReadyState == RTCDataChannelState.Open);

            MistStats.I.TotalSendBytes += data.Length;
            _dataChannel.Send(data);
        }

        public void Close()
        {
            // DataChannelを閉じる
            _dataChannel.Close();
        }

        private void OnIceConnectionChange(RTCIceConnectionState state)
        {
            Debug.Log($"[Signaling][OnIceConnectionChange] {state} -> {Id}");

            if (state == RTCIceConnectionState.Connected)
            {
            }
            else if (state == RTCIceConnectionState.Disconnected)
            {
                // TODO: Disconnectedの処理を追加する
                OnDisconnected?.Invoke(Id);
            }
        }

        private void OnOpenDataChannel()
        {
            Debug.Log($"[Signaling][DataChannel] Open -> {Id}");
            OnConnected?.Invoke(Id);
        }

        private void OnCloseDataChannel()
        {
            Debug.Log($"[Signaling][DataChannel] Finalize -> {Id}");
            // PeerConnectionを閉じる
            Connection.Close();
        }

        private void OnMessageDataChannel(byte[] data)
        {
            MistStats.I.TotalReceiveBytes += data.Length;
            OnMessage?.Invoke(data, Id);
        }

        private void OnIceCandidate(RTCIceCandidate candidate)
        {
            Debug.Log($"[Signaling][OnIceCandidate] -> {Id}");
            OnCandidate?.Invoke(new Ice(candidate));
        }

        private async UniTask Reconnect()
        {
            await UniTask.Delay(TimeSpan.FromSeconds(WaitReconnectTimeSec));
            CreateOffer().Forget();
            MistPeerData.I.SetState(Id, MistPeerState.Connecting);
        }
    }

    [System.Serializable]
    public class Ice
    {
        public string Candidate;
        public string SdpMid;
        public int SdpMLineIndex;

        public Ice(RTCIceCandidate candidate)
        {
            this.Candidate = candidate.Candidate;
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
}