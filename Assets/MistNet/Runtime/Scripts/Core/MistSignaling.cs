using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;

namespace MistNet
{
    public class MistSignaling
    {
        public Action<Dictionary<string, object>, string> Send;
        private readonly HashSet<string> _candidateData = new();

        public void SendSignalingRequest()
        {
            var sendData = CreateSendData();
            sendData.Add("type", "signaling_request");
            Send(sendData, "");
        }

        public void ReceiveSignalingResponse(Dictionary<string, object> message)
        {
            if (message["request"].ToString() == "offer")
            {
                var targetId = message["target_id"].ToString();
                SendOffer(targetId).Forget();
            }
        }

        /// <summary>
        /// ★send offer → receive answer
        /// </summary>
        /// <returns></returns>
        public async UniTask SendOffer(string targetId)
        {
            MistDebug.Log($"[MistSignaling] SendOffer: {targetId}");
            var peer = MistManager.I.MistPeerData.GetPeer(targetId);
            peer.OnCandidate = ice => SendCandidate(ice, targetId);

            var desc = await peer.CreateOffer();
            var sendData = CreateSendData();
            sendData.Add("type", "offer");
            sendData.Add("sdp", desc);
            sendData.Add("target_id", targetId);

            Send(sendData, targetId);
            // _sentOffers.Add(targetId);
        }

        /// <summary>
        /// send offer → ★receive answer
        /// </summary>
        /// <param name="response"></param>
        public void ReceiveAnswer(Dictionary<string, object> response)
        {
            var targetId = response["id"].ToString();
            var peer = MistManager.I.MistPeerData.GetPeer(targetId);
            
            var sdpString = response["sdp"]?.ToString();
            if (string.IsNullOrEmpty(sdpString))
            {
                MistDebug.LogError("sdp is null or empty");
                return;
            }
            MistDebug.Log($"[MistSignaling][SignalingState] {peer.SignalingState}");
            if (peer.SignalingState == MistSignalingState.NegotiationCompleted) return;
            if (peer.SignalingState == MistSignalingState.InitialStable) return;

            var sdp = JsonUtility.FromJson<RTCSessionDescription>(sdpString);
            peer.SetRemoteDescription(sdp).Forget();
        }

        /// <summary>
        /// ★receive offer → send answer
        /// </summary>
        /// <param name="response"></param>
        /// <returns></returns>
        public void ReceiveOffer(Dictionary<string, object> response)
        {
            var targetId = response["id"].ToString();

            // if (_processedOffers.Contains(targetId))
            // {
            //     MistDebug.LogWarning($"[MistSignaling] Offer already processed from: {targetId}");
            //     return;
            // }

            var peer = MistManager.I.MistPeerData.GetPeer(targetId);
            if (peer.SignalingState == MistSignalingState.NegotiationCompleted) return;
            
            peer.OnCandidate = (ice) => SendCandidate(ice, targetId);

            MistDebug.Log($"[MistSignaling][SignalingState] {peer.Connection.SignalingState}");
            var sdp = JsonUtility.FromJson<RTCSessionDescription>(response["sdp"].ToString());
            SendAnswer(peer, sdp, targetId).Forget();
            // _processedOffers.Add(targetId);
        }

        /// <summary>
        /// receive offer → ★send answer
        /// </summary>
        /// <param name="peer"></param>
        /// <param name="sdp"></param>
        /// <returns></returns>
        private async UniTask SendAnswer(MistPeer peer, RTCSessionDescription sdp, string targetId)
        {
            var desc = await peer.CreateAnswer(sdp);

            var sendData = CreateSendData();
            sendData.Add("type", "answer");
            sendData.Add("sdp", desc);
            sendData.Add("target_id", targetId);
            Send(sendData, targetId);

            if (_candidateData.Count == 0) return;
            foreach (var candidate in _candidateData)
            {
                var value = JsonUtility.FromJson<Ice>(candidate);
                peer.AddIceCandidate(value);
            }
            
            // 接続が完了したら、関連するオファーを削除
            // _processedOffers.Remove(targetId);
            // _sentOffers.Remove(targetId);
        }

        private void SendCandidate(Ice candidate, string targetId = "")
        {
            var candidateString = JsonUtility.ToJson(candidate);
            if (_candidateData.Contains(candidateString))
            {
                MistDebug.Log($"[MistSignaling] Candidate already sent: {candidateString}");
                return;
            }

            var sendData = CreateSendData();
            sendData.Add("type", "candidate_add");
            sendData.Add("candidate", candidateString);
            sendData.Add("target_id", targetId);
            Send(sendData, targetId);
            _candidateData.Add(candidateString);
            
            // 接続が完了したら、関連するICE候補を削除
            var peer = MistManager.I.MistPeerData.GetPeer(targetId).Connection;
            RegisterIceConnectionChangeHandler(targetId, peer);
        }

        public async void ReceiveCandidate(Dictionary<string, object> response)
        {
            var targetId = response["id"].ToString();
            var dataStr = response["candidate"].ToString();

            MistPeer peer;
            while (true)
            {
                peer = MistManager.I.MistPeerData.GetPeer(targetId);
                if (peer != null) break;
                await UniTask.Yield();
            }

            var candidates = dataStr.Split("|");

            foreach (var candidate in candidates)
            {
                if (_candidateData.Contains(candidate))
                {
                    MistDebug.Log($"[MistSignaling] Candidate already processed: {candidate}");
                    continue;
                }

                var value = JsonUtility.FromJson<Ice>(candidate);
                _candidateData.Add(candidate);

                peer.AddIceCandidate(value);
            }
        }

        /// <summary>
        /// 送信用データを作成する
        /// </summary>
        /// <returns></returns>
        private Dictionary<string, object> CreateSendData()
        {
            var sendData = new Dictionary<string, object>
            {
                { "id", MistManager.I.MistPeerData.SelfId },
            };

            return sendData;
        }
        
        private void RegisterIceConnectionChangeHandler(string targetId, RTCPeerConnection peer)
        {
            void PeerOnIceConnectionChange(RTCIceConnectionState state)
            {
                if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
                {
                    _candidateData.RemoveWhere(c => c.Contains($"\"target_id\":\"{targetId}\""));
                }

                if (state == RTCIceConnectionState.Closed || state == RTCIceConnectionState.Failed || state == RTCIceConnectionState.Disconnected)
                {
                    // 接続が切断された場合の処理を追加
                    peer.OnIceConnectionChange -= PeerOnIceConnectionChange;
                }
            }

            peer.OnIceConnectionChange += PeerOnIceConnectionChange;
        }
    }
}