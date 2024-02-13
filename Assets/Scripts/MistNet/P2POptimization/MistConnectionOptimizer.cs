using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MemoryPack;
using UnityEngine;

namespace MistNet
{
    /// <summary>
    /// 接続先を最適化する
    /// TODO: Limitを超えている際に自動切断させる
    /// TODO: Userが移動することを考慮できていない
    /// </summary>
    public class MistConnectionOptimizer : MonoBehaviour
    {
        private static readonly float IntervalSendTableTimeSec = 5f;
        private static readonly float RemoveDisconnectTimeSec = 10f; // 切断要求を受け取ってから切断をキャンセルするまでの時間

        public static MistConnectionOptimizer I { get; private set; }
        private Chunk _currentChunk;
        private IEnumerable<ConnectionElement> _sortedPeerList;

        private class ConnectionElement
        {
            public string Id { get; set; }
            public float Distance { get; set; }
            public (int, int, int) Chunk { get; set; }
        }

        private void Awake()
        {
            I = this;
        }

        private void Start()
        {
            MistManager.I.Register(MistNetMessageType.DisconnectRequest, OnDisconnectRequest);
            MistManager.I.Register(MistNetMessageType.DisconnectResponse, OnDisconnectResponse);
            MistManager.I.Register(MistNetMessageType.PeerData, OnPeerTableResponse);

            SendPeerTableWithDelay().Forget();
        }

        private void OnPeerTableResponse(byte[] data, string sourceId, string senderId)
        {
            Debug.Log($"[PeerDataResponse] {sourceId} -> received");

            var message = MemoryPackSerializer.Deserialize<P_PeerData>(data);
            MistManager.I.RoutingTable.Add(message.Id, senderId);

            if (string.IsNullOrEmpty(message.Id)) return;
            if (message.Id == MistManager.I.MistPeerData.SelfId) return;

            MistPeerData.I.UpdatePeerData(message.Id, message);

            OptimizeHandler();
            ShowRoutingTable();
        }

        /// <summary>
        /// 接続するかどうか判断を行い、必要であれば接続要求を送る
        /// </summary>
        private void OptimizeHandler()
        {
            var selfPosition = MistSyncManager.I.SelfSyncObject.transform.position;

            // 距離を計算し、Peerリストを取得
            var allPeerList = GetPeerListCalcDistance(selfPosition);
            if (!allPeerList.Any()) return; // 有効な要素がなければ終了

            // 要素を距離に基づいてソート
            _sortedPeerList = SortPeerListByDistance(allPeerList);
            Debug.Log("[Debug] ソート完了");
            
            var debugText = "[SortedTable]\n";
            int peerCount = 0;
            foreach (var element in _sortedPeerList)
            {
                var id = element.Id;
                if (string.IsNullOrEmpty(id)) continue;

                var peerData = MistPeerData.I.GetPeerData(id);
                debugText += $"{id} {peerData.State} {peerData.CurrentConnectNum} {peerData.MaxConnectNum} {element.Distance}\n";

                if (peerCount <= MistConfig.LimitConnection)
                {
                    if (peerData.State == MistPeerState.Disconnected &&
                        peerData.CurrentConnectNum < peerData.MaxConnectNum)
                    {
                        SendConnectRequest(id);
                        Debug.Log("[Debug] SendConnectRequest");
                    }
                }
                else if (peerData.State == MistPeerState.Connected)
                {
                    SendDisconnectRequest(id);
                    Debug.Log("[Debug] SendDisconnectRequest");
                } 

                peerCount++;
            }
            Debug.Log(debugText);
        }

        private void ShowRoutingTable()
        {
            var text = "[RoutingTable]\n";
            var table = MistPeerData.I.GetAllPeer;
            foreach (var kv in table)
            {
                text += $"{kv.Key} {kv.Value.Chunk} {kv.Value.CurrentConnectNum} {kv.Value.MaxConnectNum}\n";
            }

            Debug.Log(text);
        }

        /// <summary>
        /// 距離を計算し、リストを返すメソッド
        /// </summary>
        /// <param name="table"></param>
        /// <param name="selfPosition"></param>
        /// <returns></returns>
        private List<ConnectionElement> GetPeerListCalcDistance(Vector3 selfPosition)
        {
            var table = MistPeerData.I.GetAllPeer;
            return table
                .Select(element => new ConnectionElement
                {
                    Id = element.Value.Id,
                    Distance = Vector3.Distance(selfPosition, element.Value.Position), // 自分の位置との距離を計算
                    Chunk = element.Value.Chunk
                })
                .ToList();
        }

        /// <summary>
        /// 要素を距離に基づいてソートするメソッド 
        /// </summary>
        /// <param name="elements"></param>
        /// <returns></returns>
        private IEnumerable<ConnectionElement> SortPeerListByDistance(IEnumerable<ConnectionElement> elements)
        {
            return elements.OrderBy(x => x.Distance);
        }

        private void SendDisconnectRequest(string id)
        {
            AddDisconnectListAndDelayCancel(id).Forget();

            var message = new P_DisconnectRequest();
            var data = MemoryPackSerializer.Serialize(message);
            MistManager.I.Send(MistNetMessageType.DisconnectRequest, data, id);
        }

        /// <summary>
        /// 相手からの切断要求を受け取る
        /// </summary>
        /// <param name="data"></param>
        /// <param name="sourceId"></param>
        /// <param name="_"></param>
        private void OnDisconnectRequest(byte[] data, string sourceId, string _)
        {
            var disconnectList = MistOptimizationManager.I.Data.PeerDisconnectRequestList;
            if (disconnectList.Contains(sourceId))
            {
                // お互いに切断対象が同じである場合
                if (CompareId(sourceId)) return;    // どちらが切断するかを決める

                // 切断許可を返す
                var message = new P_DisconnectResponse();
                var bytes = MemoryPackSerializer.Serialize(message);
                MistManager.I.Send(MistNetMessageType.DisconnectResponse, bytes, sourceId);

                // return;
            }
            
            // 切断リストに追加する
            // AddDisconnectListAndDelayCancel(sourceId).Forget();
        }

        private void OnDisconnectResponse(byte[] data, string sourceId, string _)
        {
            if (!IsConnectionAtLimit()) return;

            // 切断する
            MistManager.I.Disconnect(sourceId);
            var disconnectList = MistOptimizationManager.I.Data.PeerDisconnectRequestList;
            if (disconnectList.Contains(sourceId))
            {
                disconnectList.Remove(sourceId);
            }
        }

        private async UniTaskVoid AddDisconnectListAndDelayCancel(string id)
        {
            var disconnectList = MistOptimizationManager.I.Data.PeerDisconnectRequestList;
            disconnectList.Add(id);

            var peerData = MistPeerData.I.GetPeerData(id);
            peerData.State = MistPeerState.Disconnecting;

            await UniTask.Delay(TimeSpan.FromSeconds(RemoveDisconnectTimeSec));

            // time out処理
            if (disconnectList.Contains(id))
            {
                disconnectList.Remove(id);
                peerData.State = MistPeerState.Connected;
            }
        }
        
        /// <summary>
        /// 接続要求を送る
        /// </summary>
        /// <param name="id"></param>
        private void SendConnectRequest(string id)
        {
            if (id == MistManager.I.MistPeerData.SelfId) return; // 自分自身には接続しない
            if (CompareId(id)) return;

            Debug.Log($"[ConnectRequest] {MistManager.I.MistPeerData.SelfId} -> {id}");
            MistManager.I.Connect(id).Forget();
        }
        
        private async UniTaskVoid SendPeerTableWithDelay(CancellationToken token = default)
        {
            while (!token.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(IntervalSendTableTimeSec), cancellationToken: token);
                SendPeerData().Forget();
            }
        }

        private async UniTaskVoid SendPeerData(string id = "")
        {
            List<byte[]> sendList = new();
            if (MistManager.I.MistPeerData.SelfId == null)
            {
                Debug.LogError("SelfId is null");
            }

            // 自身の情報
            var selfData = new P_PeerData
            {
                Id = MistManager.I.MistPeerData.SelfId,
                Chunk = MistSyncManager.I.SelfSyncObject.Chunk.Get(),
                Position = MistSyncManager.I.SelfSyncObject.transform.position,
                CurrentConnectNum = MistManager.I.MistPeerData.GetConnectedPeer.Count,
                MinConnectNum = MistConfig.MinConnection,
                LimitConnectNum = MistConfig.LimitConnection,
                MaxConnectNum = MistConfig.MaxConnection,
            };
            var selfBytes = MemoryPackSerializer.Serialize(selfData);
            sendList.Add(selfBytes);

            // 他のPeerの情報
            foreach (var element in MistPeerData.I.GetAllPeer)
            {
                if (string.IsNullOrEmpty(element.Value.Id)) continue;
                var chunkStr = ParseChunk(element.Value.Chunk);
                if(element.Value.MaxConnectNum == 0) continue;
                if(element.Value.State == MistPeerState.Disconnected) continue;
                
                var sendData = new P_PeerData()
                {
                    Id = element.Value.Id,
                    Chunk = chunkStr,
                    Position = element.Value.Position,
                    CurrentConnectNum = element.Value.CurrentConnectNum,
                    MinConnectNum = element.Value.MinConnectNum,
                    LimitConnectNum = element.Value.LimitConnectNum,
                    MaxConnectNum = element.Value.MaxConnectNum,
                };
                var bytes = MemoryPackSerializer.Serialize(sendData);
                sendList.Add(bytes);
            }

            foreach (var sendData in sendList)
            {
                if (string.IsNullOrEmpty(id)) MistManager.I.SendAll(MistNetMessageType.PeerData, sendData);
                else MistManager.I.Send(MistNetMessageType.PeerData, sendData, id);
                await UniTask.Yield();
            }
        }
        
        private bool CompareId(string sourceId)
        {
            var selfId = MistManager.I.MistPeerData.SelfId;
            if (String.CompareOrdinal(selfId, sourceId) < 0) return true;
            return false;
        }
        
        private bool IsConnectionAtLimit()
        {
            return MistManager.I.MistPeerData.GetConnectedPeer.Count >= MistConfig.LimitConnection;
        }
        
        private string ParseChunk((int, int, int) valueChunk)
        {
            return $"{valueChunk.Item1},{valueChunk.Item2},{valueChunk.Item3}";
        }
    }
}