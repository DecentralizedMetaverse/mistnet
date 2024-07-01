using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System.Net.Sockets;

namespace MistNet
{
    public class MistSignalingServer : MonoBehaviour
    {
        private static readonly int DefaultPort = 8080;
        [SerializeField] private bool isServerMode = false;
        private WebSocketServer _webSocketServer;

        private void Start()
        {
            if (!isServerMode) return;

            int port = FindAvailablePort(DefaultPort);
            if (port == -1)
            {
                MistDebug.LogError("[MistSignalingServer] No available port found.");
                return;
            }

            _webSocketServer = new WebSocketServer(port);
            _webSocketServer.AddWebSocketService<MistWebSocketBehavior>("/ws");
            _webSocketServer.Start();
            MistDebug.Log($"[MistSignalingServer] Start on port {port}");
        }

        private void OnDestroy()
        {
            if (_webSocketServer == null) return;
            _webSocketServer.Stop();
            MistDebug.Log($"[MistSignalingServer] Stop");
        }

        private int FindAvailablePort(int startingPort)
        {
            const int maxAttempts = 100;
            for (int port = startingPort; port < startingPort + maxAttempts; port++)
            {
                if (IsPortAvailable(port))
                {
                    return port;
                }
            }
            return -1;
        }

        private bool IsPortAvailable(int port)
        {
            try
            {
                TcpListener listener = new TcpListener(System.Net.IPAddress.Any, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private class MistWebSocketBehavior : WebSocketBehavior
        {
            private static Dictionary<string, string> sessionIdToClientId = new();
            private static ConcurrentQueue<string> signalingRequestIds = new();

            protected override void OnOpen()
            {
                MistDebug.Log($"[SERVER][OPEN] {ID}");
                signalingRequestIds.Enqueue(ID);
            }

            protected override void OnClose(CloseEventArgs e)
            {
                MistDebug.Log($"[SERVER][CLOSE] {ID}");
                sessionIdToClientId.Remove(ID);

                var newList = signalingRequestIds.Where(x => x != ID).ToList();
                signalingRequestIds = new ConcurrentQueue<string>(newList);
            }

            protected override void OnMessage(MessageEventArgs e)
            {
                MistDebug.Log($"[SERVER][RECV] {e.Data}");

                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(e.Data);
                var messageType = data["type"].ToString();
                if (!sessionIdToClientId.ContainsKey(ID))
                {
                    var id = data["id"].ToString();
                    sessionIdToClientId.TryAdd(ID, id);
                }

                if (messageType == "signaling_request")
                {
                    HandleSignalingRequest();
                }
                else
                {
                    var targetId = data["target_id"].ToString();
                    var targetSessionId = sessionIdToClientId.FirstOrDefault(x => x.Value == targetId).Key;
                    if (!string.IsNullOrEmpty(targetSessionId))
                    {
                        Sessions.SendTo(e.Data, targetSessionId);
                    }
                }
            }

            private void HandleSignalingRequest()
            {
                var availableSessionIds = signalingRequestIds.Where(id => id != ID).ToList();
                if (availableSessionIds.Count > 0)
                {
                    var random = new System.Random();
                    var targetSessionId = availableSessionIds[random.Next(availableSessionIds.Count)];

                    if (sessionIdToClientId.TryGetValue(targetSessionId, out var targetClientId))
                    {
                        var response = new { type = "signaling_response", target_id = targetClientId, request = "offer" };
                        var sendData = JsonConvert.SerializeObject(response);
                        Sessions.SendTo(sendData, ID);
                    }
                }
            }
        }
    }
}
