using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MemoryPack;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MistNet
{
    /// <summary>
    /// 接続先を最適化する
    /// TODO: Limitを超えている際に自動切断させる
    /// TODO: Userが移動することを考慮できていない
    /// </summary>
    public class MistConnectionOptimizer : MonoBehaviour
    {
        private static readonly float IntervalSendTableTimeSec = 1.0f;
        private static readonly float RemoveDisconnectTimeSec = 10f; // 切断要求を受け取ってから切断をキャンセルするまでの時間
        private static readonly float BlockConnectIntervalTimeSec = 3f; // 一定時間接続をブロックする
        
        public static MistConnectionOptimizer I { get; private set; }
        private IEnumerable<ConnectionElement> _sortedPeerList;
        private CancellationTokenSource _cancelTokenSource;

        private float _leaderTimeMax;
        private float _leaderTime;

        private class ConnectionElement
        {
            public string Id { get; set; }
            public float Distance { get; set; }
        }

        private void Awake()
        {
            I = this;
        }

        private void Start()
        {
            _leaderTimeMax = Random.Range(2.0f, 4.0f);
            _leaderTime = _leaderTimeMax;
            
            MistManager.I.AddRPC(MistNetMessageType.DisconnectRequest, OnDisconnectRequest);
            MistManager.I.AddRPC(MistNetMessageType.DisconnectResponse, OnDisconnectResponse);
            MistManager.I.AddRPC(MistNetMessageType.PeerData, OnPeerTableResponse);
            MistManager.I.AddRPC(MistNetMessageType.LeaderNotify, (_,_) => _leaderTime = _leaderTimeMax);
            
            _cancelTokenSource = new();
            SendPeerTableWithDelay(_cancelTokenSource.Token).Forget();
            UpdateRoutingTable(_cancelTokenSource.Token).Forget();
            UpdateLeaderTime(_cancelTokenSource.Token).Forget();
        }

        private async UniTaskVoid UpdateLeaderTime(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await UniTask.Yield();
                _leaderTime -= Time.deltaTime;
                if (_leaderTime < 0) _leaderTime = 0;
            }
        }

        private void OnDestroy()
        {
            _cancelTokenSource.Cancel();
        }
        
        public void OnDisconnected(string id)
        {
            var peerData = MistPeerData.I.GetPeerData(id);
            peerData.State = MistPeerState.Disconnected;
            peerData.BlockConnectIntervalTime = BlockConnectIntervalTimeSec;
        }

        private void OnPeerTableResponse(byte[] data, string sourceId)
        {
            MistDebug.Log($"[PeerDataResponse] {sourceId} -> received");

            var message = MemoryPackSerializer.Deserialize<P_PeerData>(data);
            // MistManager.I.RoutingTable.Add(message.Id, senderId);

            if (string.IsNullOrEmpty(message.Id)) return;
            if (message.Id == MistManager.I.MistPeerData.SelfId) return;

            MistPeerData.I.UpdatePeerData(message.Id, message);

            OptimizeHandler();
        }

        /// <summary>
        /// 接続するかどうか判断を行い、必要であれば接続要求を送る
        /// </summary>
        private void OptimizeHandler()
        {
            if (_leaderTime > 0) return;
            
            var selfSyncObject = MistSyncManager.I.SelfSyncObject;
            if (selfSyncObject == null) return;
            var selfPosition = selfSyncObject.transform.position;

            // 距離を計算し、Peerリストを取得
            var allPeerList = GetPeerListCalcDistance(selfPosition);
            if (!allPeerList.Any()) return; // 有効な要素がなければ終了

            // 要素を距離に基づいてソート
            _sortedPeerList = SortPeerListByDistance(allPeerList);
            MistDebug.Log("[Debug] ソート完了");
            
            var debugText = "[SortedTable]\n";
            var peerCount = 0;
            foreach (var element in _sortedPeerList)
            {
                var id = element.Id;
                if (string.IsNullOrEmpty(id)) continue;

                var peerData = MistPeerData.I.GetPeerData(id);
                debugText += $"{id} {peerData.State} {peerData.CurrentConnectNum} {peerData.MaxConnectNum} {element.Distance}\n";

                // if (peerCount <= MistConfig.LimitConnection)
                {
                    if (peerData.State == MistPeerState.Disconnected) 
                        // &&
                        // peerData.CurrentConnectNum < peerData.MaxConnectNum)
                        // &&peerData.BlockConnectIntervalTime == 0)
                    {
                        SendConnectRequest(id);
                        MistDebug.Log("[Debug] SendConnectRequest");
                        SendLeaderNotify();
                    }
                }
                // if (peerData.State == MistPeerState.Connected && peerData.CurrentConnectNum > peerData.MinConnectNum)
                // {
                //     SendDisconnectRequest(id);
                //     MistDebug.Log("[Debug] SendDisconnectRequest");
                //     SendLeaderNotify();
                // } 

                peerCount++;
            }
            MistDebug.Log(debugText);
        }

        private async UniTaskVoid UpdateRoutingTable(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(1.0f), cancellationToken: token);
                SendPeerData().Forget();

                var text = "[RoutingTable]\n";
                var table = MistPeerData.I.GetAllPeer;
                foreach (var kv in table)
                {
                    text += $"{kv.Key} {kv.Value.CurrentConnectNum} {kv.Value.MaxConnectNum} {kv.Value.State}\n";
                    kv.Value.BlockConnectIntervalTime--;
                    if (kv.Value.BlockConnectIntervalTime < 0)
                    {
                        kv.Value.BlockConnectIntervalTime = 0;
                    }
                }
                MistDebug.Log(text);
            }
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
            // AddDisconnectListAndDelayCancel(id).Forget();

            // var message = new P_DisconnectRequest();
            // var data = MemoryPackSerializer.Serialize(message);
            // MistManager.I.Send(MistNetMessageType.DisconnectRequest, data, id);
            
            // 切断許可を返す
            var message = new P_DisconnectResponse();
            var bytes = MemoryPackSerializer.Serialize(message);
            MistManager.I.Send(MistNetMessageType.DisconnectResponse, bytes, id);
        }

        /// <summary>
        /// 相手からの切断要求を受け取る
        /// </summary>
        /// <param name="data"></param>
        /// <param name="sourceId"></param>
        /// <param name="_"></param>
        private void OnDisconnectRequest(byte[] data, string sourceId)
        {
            var disconnectList = MistOptimizationManager.I.Data.PeerDisconnectRequestList;
            if (disconnectList.Contains(sourceId))　// 自身も切断しようとしていたかどうか
            {
                // お互いに切断対象が同じである場合
                if (CompareId(sourceId)) return;    // どちらが切断するかを決める

                // 切断許可を返す
                var message = new P_DisconnectResponse();
                var bytes = MemoryPackSerializer.Serialize(message);
                MistManager.I.Send(MistNetMessageType.DisconnectResponse, bytes, sourceId);

                return;
            }
            
            // 切断リストに追加する
            AddDisconnectListAndDelayCancel(sourceId).Forget();
        }

        private void OnDisconnectResponse(byte[] data, string sourceId)
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
            // if (CompareId(id)) return;

            MistDebug.Log($"[ConnectRequest] {MistManager.I.MistPeerData.SelfId} -> {id}");
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
                MistDebug.LogError("SelfId is null");
            }

            // 自身の情報
            var selfData = new P_PeerData
            {
                Id = MistManager.I.MistPeerData.SelfId,
                Position = MistSyncManager.I.SelfSyncObject.transform.position,
                CurrentConnectNum = MistManager.I.MistPeerData.GetConnectedPeer.Count,
                LimitConnectNum = MistConfig.LimitConnection,
                MaxConnectNum = MistConfig.MaxConnection,
            };
            var selfBytes = MemoryPackSerializer.Serialize(selfData);
            sendList.Add(selfBytes);

            // 他のPeerの情報
            foreach (var element in MistPeerData.I.GetAllPeer)
            {
                if (string.IsNullOrEmpty(element.Value.Id)) continue;
                if(element.Value.MaxConnectNum == 0) continue;
                if(element.Value.State == MistPeerState.Disconnected) continue;
                
                var sendData = new P_PeerData()
                {
                    Id = element.Value.Id,
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

        private void SendLeaderNotify()
        {
            var message = new P_LeaderNotify();
            var bytes = MemoryPackSerializer.Serialize(message);
            MistManager.I.SendAll(MistNetMessageType.LeaderNotify, bytes);
        }
    }
}