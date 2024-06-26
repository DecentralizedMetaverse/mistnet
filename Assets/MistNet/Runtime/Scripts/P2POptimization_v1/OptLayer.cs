using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MistNet.Opt
{
    public class OptLayer : MonoBehaviour
    {
        private const float UpdateTimeSec = 3.0f;
        public static OptLayer I { get; private set; }

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
        }

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

        private void OnDestroy()
        {
            MistManager.I.OnConnectedAction -= OnConnected;
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
    }
}