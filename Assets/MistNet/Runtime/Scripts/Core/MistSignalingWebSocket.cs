using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using WebSocketSharp;

namespace MistNet
{
    public class MistSignalingWebSocket : MonoBehaviour
    {
        public static MistSignalingWebSocket I;
        public WebSocket Ws => _ws;
        
        private Dictionary<string, Action<Dictionary<string, object>>> _functions;
        private WebSocket _ws;
        private MistSignaling _mistSignaling;
        private Queue<string> _messageQueue = new();

        private void Start()
        {
            I = this;
            _messageQueue.Clear();
            _mistSignaling = new MistSignaling();
            _mistSignaling.Send += Send;

            // Functionの登録
            _functions = new()
            {
                { "signaling_response", _mistSignaling.ReceiveSignalingResponse},
                { "offer", _mistSignaling.ReceiveOffer },
                { "answer", _mistSignaling.ReceiveAnswer },
                { "candidate_add", _mistSignaling.ReceiveCandidate },
            };
            
            // 接続
            ConnectToSignalingServer();

            // Check
            _mistSignaling.SendSignalingRequest();
        }

        private void OnDestroy()
        {
            if (_ws != null)
            {
                _ws.Close();
            }
        }

        private void Update()
        {
            if (_messageQueue.Count == 0) return;
            
            var text = _messageQueue.Dequeue();
            var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(text);
            var type = response["type"].ToString();
            _functions[type](response);
        }

        private void ConnectToSignalingServer()
        {
            _ws = new WebSocket(MistConfig.SignalingServerAddress);
            _ws.OnOpen += (sender, e) => { MistDebug.Log("[WebSocket] Connected"); };
            _ws.OnClose += (sender, e) => { MistDebug.Log("[WebSocket] Closed"); };
            _ws.OnMessage += (sender, e) =>
            {
                _messageQueue.Enqueue(e.Data);
            };
            _ws.OnError += (sender, e) => { MistDebug.LogError($"[WebSocket] Error {e.Message}"); };
            _ws.Connect();
        }

        private void Send(Dictionary<string, object> sendData, string _)
        {
            var text = JsonConvert.SerializeObject(sendData);
            _ws.Send(text);
        }
    }
}