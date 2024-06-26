using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MemoryPack;
using UnityEngine;

namespace MistNet.Opt
{
    public class OptLayer : MonoBehaviour
    {
        private const float UpdateTimeSec = 3.0f;
        public static OptLayer I { get; private set; }

        private readonly Dictionary<string, Node> _nodes = new();
        private readonly Dictionary<string, NodeId> _nodeIds = new();
        private Kademlia _kademlia;

        // 同じChunkに属するノードのIDを保存する
        private readonly Dictionary<(int, int, int), HashSet<string>> _chunkNodes = new();
        private (int, int, int) _chunk;

        private void Awake()
        {
            I = this;
        }

        [ContextMenu("RunOptLayer")]
        private void Start()
        {
            // ノードの作成
            OnConnected(MistPeerData.I.SelfId);
            MistManager.I.OnConnectedAction += OnConnected;
            UpdateGetChunk(this.GetCancellationTokenOnDestroy()).Forget();
            _kademlia.SendPing += SendPing;
            _kademlia.SendStore += SendStore;
            _kademlia.SendFindNode += SendFindNode;
            _kademlia.SendFindValue += SendFindValue;
            MistManager.I.AddRPC(MistNetMessageType.Dht, ReceiveDht);
        }

        private void OnDestroy()
        {
            MistManager.I.OnConnectedAction -= OnConnected;
            _kademlia.SendPing -= SendPing;
            _kademlia.SendStore -= SendStore;
            _kademlia.SendFindNode -= SendFindNode;
            _kademlia.SendFindValue -= SendFindValue;
        }

        private void ReceiveDht(byte[] data, string id)
        {
            var receiveData = MemoryPackSerializer.Deserialize<P_Dht>(data);
            switch (receiveData.Type)
            {
                case "Ping":
                    _kademlia.ReceivePing(GetNode(id));
                    break;
                case "Store":
                    var str  = receiveData.Data.Split('|');
                    _kademlia.ReceiveStore(GetNode(id), str[0], str[1]);
                    break;
                case "FindNode":
                    _kademlia.ReceiveFindNode(GetNode(id), GetNodeId(receiveData.Data));
                    break;
                case "FindValue":
                    _kademlia.ReceiveFindValue(GetNode(id), receiveData.Data);
                    break;
            }
        }

        private void SendPing(Node node)
        {
            var sendData = new P_Dht
            {
                Type = "Ping",
                Data = ""
            };
            var bytes = MemoryPackSerializer.Serialize(sendData);
            MistManager.I.Send(MistNetMessageType.Dht, bytes, node.Address);
        }

        private void SendStore(Node node, string key, string value)
        {
            var sendData = new P_Dht
            {
                Type = "Store",
                Data = $"{key}|{value}"
            };
            var bytes = MemoryPackSerializer.Serialize(sendData);
            MistManager.I.Send(MistNetMessageType.Dht, bytes, node.Address);
        }

        private void SendFindNode(Node node, NodeId nodeId)
        {
            var sendData = new P_Dht
            {
                Type = "FindNode",
                Data = $"{nodeId.ToString()}"
            };
            var bytes = MemoryPackSerializer.Serialize(sendData);
            MistManager.I.Send(MistNetMessageType.Dht, bytes, node.Address);
        }

        private void SendFindValue(Node node, string value)
        {
            var sendData = new P_Dht
            {
                Type = "FindValue",
                Data = $"{value}"
            };
            var bytes = MemoryPackSerializer.Serialize(sendData);
            MistManager.I.Send(MistNetMessageType.Dht, bytes, node.Address);
        }

        // --------------------

        /// <summary>
        /// 定期的にChunkデータを取得する
        /// </summary>
        private async UniTask UpdateGetChunk(CancellationToken token)
        {
            while(!token.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(UpdateTimeSec), cancellationToken: token);
                var (nodes, found) = await Find(_chunk);
                if (!found) continue;

                // 他のノードがChunkに存在する場合
                foreach (var nodeId in nodes)
                {
                    _chunkNodes[_chunk].Add(nodeId);

                    if (nodeId == MistPeerData.I.SelfId) continue;
                    MistManager.I.Connect(nodeId).Forget();
                }
            }
        }

        // --------------------

        private void OnConnected(string id)
        {
            var nodeId = new NodeId(id);
            var node = new Node(nodeId, id); // Addressの代わりにIDを使う
            if (_kademlia == null) _kademlia = new Kademlia(node);
            else _kademlia.RoutingTable.AddNode(node);
        }

        public void OnChangedChunk((int, int, int) chunk)
        {
            // 追加
            _chunkNodes.TryGetValue(chunk, out var nodes);
            if (nodes == null)
            {
                nodes = new HashSet<string>();
                _chunkNodes.Add(chunk, nodes);
            }

            nodes.Add(MistPeerData.I.SelfId);

            // 削除
            _chunkNodes[_chunk].Remove(MistPeerData.I.SelfId);
            Store(_chunk, _chunkNodes[_chunk]);

            _chunk = chunk;
            Store(chunk, nodes);
        }

        // --------------------

        private async UniTask<(HashSet<string>, bool)> Find((int, int, int) chunk)
        {
            var (data, found) = await _kademlia.FindValueAsync(ChunkToString(chunk));
            if (!found) return (null, false);

            var nodes = new HashSet<string>(Encoding.UTF8.GetString(data).Split(','));
            return (nodes, true);
        }

        private void Store((int, int, int) chunk, HashSet<string> nodes)
        {
            _kademlia.StoreAsync(ChunkToString(chunk), string.Join(",", nodes)).Forget();
        }

        private string ChunkToString((int, int, int) chunk)
        {
            return $"{chunk.Item1},{chunk.Item2},{chunk.Item3}";
        }

        // --------------------

        private Node GetNode(string id)
        {
            if (!_nodes.TryGetValue(id, out var node))
            {
                _nodes.Add(id, new Node(new NodeId(id), id));
            }
            return _nodes[id];
        }

        private NodeId GetNodeId(string id)
        {
            if (!_nodeIds.TryGetValue(id, out var nodeId))
            {
                _nodeIds.Add(id, new NodeId(id));
            }
            return _nodeIds[id];
        }
    }
}