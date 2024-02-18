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
            
            MistManager.I.AddRPC(MistNetMessageType.Signaling, ReceiveSignalingMessage);
            MistManager.I.ConnectAction += Connect;
        }
        
        /// <summary>
        /// 送信
        /// NOTE: 切断した相手にすぐに接続を試みると、nullになることがある
        /// </summary>
        /// <param name="sendData"></param>
        /// <param name="targetId"></param>
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
        private void ReceiveSignalingMessage(byte[] bytes, string sourceId, string _)
        {
            var receiveData = MemoryPackSerializer.Deserialize<P_Signaling>(bytes);
            var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(receiveData.Data);
            var type = response["type"].ToString();
            Debug.Log($"[RECV][Signaling][{type}] {sourceId} ->");
            _functions[type](response);
        }

        private void Connect(string id)
        {
            _mistSignaling.SendOffer(id).Forget();
        }
    }
}