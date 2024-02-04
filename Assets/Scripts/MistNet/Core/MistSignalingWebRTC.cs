using MemoryPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MistNet
{
    public class MistSignalingWebRTC : MonoBehaviour
    {
        private MistSignaling _mistSignaling;
        private Dictionary<string, Action<Dictionary<string, object>>> _functions;
        private readonly HashSet<string> _signalingRequestIds = new();
        
        private void Start()
        {
            _mistSignaling = new MistSignaling();
            _mistSignaling.Send += SendSignalingMessage;
            // Functionの登録
            _functions = new()
            {
                { "signaling_response", _mistSignaling.ReceiveSignalingResponse},
                { "offer", _mistSignaling.ReceiveOffer },
                { "answer", _mistSignaling.ReceiveAnswer },
                { "candidate_add", _mistSignaling.ReceiveCandidate },
            };

            // MistManager.I.Register(MistNetMessageType.JoinNotify, OnJoin);
            MistManager.I.Register(MistNetMessageType.Signaling, ReceiveSignalingMessage);
            MistManager.I.Register(MistNetMessageType.SignalingRequest, OnSignalingRequest);
            MistManager.I.Register(MistNetMessageType.SignalingResponse, OnSignalingResponse);
            MistManager.I.ConnectAction += Connect;
        }
        
        /// <summary>
        /// 送信
        /// NOTE: 切断した相手にすぐに接続を試みると、nullになることがある
        /// </summary>
        /// <param name="sendData"></param>
        /// <param name="targetId"></param>
        /// <param name="viaId"></param>
        private void SendSignalingMessage(Dictionary<string, object> sendData, string targetId)
        {
            var message = new P_Signaling
            {
                Data = JsonConvert.SerializeObject(sendData)
            };
            var data = MemoryPackSerializer.Serialize(message);
            MistManager.I.Send(MistNetMessageType.Signaling, data, targetId);
            var type = sendData["type"].ToString();
            Debug.Log($"[SEND][Signaling][{type}] -> {targetId}");
        }

        /// <summary>
        /// 受信
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="sourceId"></param>
        /// <param name="viaId"></param>
        private void ReceiveSignalingMessage(byte[] bytes, string sourceId, string _)
        {
            var receiveData = MemoryPackSerializer.Deserialize<P_Signaling>(bytes);
            var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(receiveData.Data);
            var type = response["type"].ToString();
            Debug.Log($"[RECV][Signaling][{type}] {sourceId} ->");
            _functions[type](response);
        }

        [Obsolete]
        private void OnSignalingRequest(byte[] data, string sourceId, string senderId)
        {
            var message = MemoryPackSerializer.Deserialize<P_SignalingRequest>(data);
            if (_signalingRequestIds.Contains(message.TargetId))
            {
                Debug.Log($"request check: {sourceId}, {message.TargetId}");
                // 誰がofferを送るかの決定
                var sendData = new P_SignalingResponse
                {
                    TargetId = message.TargetId,
                    Request = "",
                };
                MistManager.I.Send(MistNetMessageType.SignalingResponse, MemoryPackSerializer.Serialize(sendData), sourceId);
                
                sendData.TargetId = senderId;
                sendData.Request = "offer";
                MistManager.I.Send(MistNetMessageType.SignalingResponse, MemoryPackSerializer.Serialize(sendData), message.TargetId);

                _signalingRequestIds.Remove(message.TargetId);
            }
            else
            {
                Debug.Log($"request added: {sourceId}, {senderId}, {message.TargetId}");
                _signalingRequestIds.Add(sourceId);
            }
        }
        
        [Obsolete]
        private void OnSignalingResponse(byte[] data, string sourceId, string senderId)
        {
            var message = MemoryPackSerializer.Deserialize<P_SignalingResponse>(data);
            if (message.Request == "offer")
            {
                Debug.Log($"send offer: {sourceId}, {senderId}, {message.TargetId}");
                _mistSignaling.SendOffer(message.TargetId).Forget();
            }
        }

        private void Connect(string id)
        {
            _mistSignaling.SendOffer(id).Forget();
        }
    }
}