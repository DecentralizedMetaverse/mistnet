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
            var peer = MistManager.I.MistPeerData.GetPeer(targetId);
            peer.OnCandidate = (ice) => SendCandidate(ice, targetId);

            var sdp = JsonUtility.FromJson<RTCSessionDescription>(response["sdp"].ToString());
            SendAnswer(peer, sdp, targetId).Forget();
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
        }

        private void SendCandidate(Ice candidate, string targetId = "")
        {
            var sendData = CreateSendData();
            sendData.Add("type", "candidate_add");
            sendData.Add("candidate", candidate);
            sendData.Add("target_id", targetId);
            Send(sendData, targetId);
        }

        public async void ReceiveCandidate(Dictionary<string, object> response)
        {
            var targetId = response["id"].ToString();
            var dataStr = response["candidate"].ToString();
            
            MistPeer peer = null;
            while (true)
            {
                peer = MistManager.I.MistPeerData.GetPeer(targetId);
                if (peer != null) break;
                await UniTask.Yield();
            }

            var candidates = dataStr.Split("|");

            foreach (var candidate in candidates)
            {
                if (_candidateData.Contains(candidate)) continue;

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
    }
}