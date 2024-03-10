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
        private static readonly int BlockConnectIntervalTimeSec = 10; // 一定時間接続をブロックする

        public static MistConnectionOptimizer I { get; private set; }
        private IEnumerable<MistPeerDataElement> _sortedPeerList;
        private CancellationTokenSource _cancelTokenSource;

        private float _leaderTimeMax;
        private float _leaderTime;
        private float _decreaseSpeed;
        private bool _isLeader;

        private void Awake()
        {
            I = this;
        }

        private void Start()
        {
            InitLeaderTime();

            // MistManager.I.AddRPC(MistNetMessageType.DisconnectRequest, OnDisconnectRequest);
            // MistManager.I.AddRPC(MistNetMessageType.DisconnectResponse, OnDisconnectResponse);
            MistManager.I.AddRPC(MistNetMessageType.PeerData, OnPeerTableResponse);
            MistManager.I.AddRPC(MistNetMessageType.LeaderNotify, (_, _) =>
            {
                InitLeaderTime();
                _isLeader = false;
            });

            _cancelTokenSource = new();
            SendPeerTableWithDelay(_cancelTokenSource.Token).Forget();
            UpdateLeaderTime(_cancelTokenSource.Token).Forget();
        }

        private void InitLeaderTime()
        {
            _leaderTimeMax = Random.Range(1.5f, 2.5f);
            _decreaseSpeed = Random.Range(0.1f, 0.5f);
            _leaderTime = _leaderTimeMax;
        }

        private async UniTaskVoid UpdateLeaderTime(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await UniTask.Yield();
                _leaderTime -= Time.deltaTime * _decreaseSpeed;
                if (_leaderTime < 0)
                {
                    _leaderTime = 0;
                }

                if (_leaderTime == 0 && !_isLeader)
                {
                    SendLeaderNotify();
                    _isLeader = true;
                }

                // MistDebug.Log($"[Info] {_leaderTime}");
            }
        }
        
        private async UniTaskVoid UpdateBlockConnectIntervalTime(string id, CancellationToken token)
        {
            var peerData = MistPeerData.I.GetPeerData(id);
            peerData.BlockConnectIntervalTime = BlockConnectIntervalTimeSec;
            while (peerData.BlockConnectIntervalTime > 0)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
                peerData.BlockConnectIntervalTime --;
            }
        }

        private void OnDestroy()
        {
            _cancelTokenSource.Cancel();
        }

        public void OnDisconnected(string id)
        {
            MistPeerData.I.SetState(id, MistPeerState.Disconnected);
            MistPeerData.I.GetPeerData(id).BlockConnectIntervalTime = BlockConnectIntervalTimeSec;
            UpdateBlockConnectIntervalTime(id, _cancelTokenSource.Token).Forget();
        }

        private void OnPeerTableResponse(byte[] data, string sourceId)
        {
            MistDebug.Log($"[PeerDataResponse] {sourceId} -> received");

            var message = MemoryPackSerializer.Deserialize<P_PeerData>(data);
            MistManager.I.RoutingTable.Add(message.Id, sourceId);

            if (string.IsNullOrEmpty(message.Id)) return;
            if (message.Id == MistManager.I.MistPeerData.SelfId) return;

            if (!MistPeerData.I.UpdatePeerData(message.Id, message)) return;

            OptimizeHandler();
        }

        /// <summary>
        /// 接続するかどうか判断を行い、必要であれば接続要求を送る
        /// </summary>
        private void OptimizeHandler()
        {
            MistDebug.Log($"[Info] {_leaderTime}/{_leaderTimeMax}");

            var selfSyncObject = MistSyncManager.I.SelfSyncObject;
            if (selfSyncObject == null) return;
            var selfPosition = selfSyncObject.transform.position;

            // 距離を計算
            var allPeerTable = MistPeerData.I.GetAllPeer;
            foreach (var element in allPeerTable.Values)
            {
                element.Distance = Vector3.Distance(selfPosition, element.Position);
            }

            // 要素を距離に基づいてソート
            var sortedPeerList = allPeerTable.Values.OrderBy(x => x.Distance).ToList();

            var debugText = $"[Info]\n";
            var peerCount = 0;
            var connectCount = 0;
            foreach (var element in sortedPeerList)
            {
                var id = element.Id;
                if (string.IsNullOrEmpty(id)) continue;

                var peerData = MistPeerData.I.GetPeerData(id);
                debugText +=
                    $"[{id}] {peerData.State} {peerData.CurrentConnectNum} {peerData.MaxConnectNum} {element.Position} {element.Distance} {element.BlockConnectIntervalTime}\n";

                if (peerCount <= MistConfig.LimitConnection)
                {
                    connectCount++;
                    if (_leaderTime > 0)
                    {
                        peerCount++;
                        continue; // 権利がない場合は接続しない
                    }

                    if (peerData.State == MistPeerState.Disconnected &&
                        peerData.CurrentConnectNum < peerData.MaxConnectNum &&
                        peerData.BlockConnectIntervalTime <= 0)
                    {
                        SendConnectRequest(id);
                        MistDebug.Log($"[Info][Debug] SendConnectRequest {id}");
                        SendLeaderNotify();
                    }
                }
                else if (peerData.State == MistPeerState.Connected &&
                         peerData.CurrentConnectNum > peerData.MinConnectNum&&
                         connectCount > MistConfig.LimitConnection)
                {
                    SendDisconnectRequest(id);
                    MistDebug.Log($"[Info][Debug] SendDisconnectRequest {id}");
                }

                peerCount++;
            }

            MistDebug.Log(debugText);
        }

        private void SendDisconnectRequest(string id)
        {
            if (CompareId(id)) return; // どちらが切断するかを決める
            MistManager.I.Disconnect(id);
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
            // SendLeaderNotify();
        }

        private async UniTaskVoid SendPeerTableWithDelay(CancellationToken token = default)
        {
            while (!token.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(IntervalSendTableTimeSec), cancellationToken: token);
                SendPeerData().Forget();
            }
        }

        /// <summary>
        /// </summary>
        private async UniTaskVoid SendPeerData()
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
                if (element.Value.MaxConnectNum == 0) continue;
                if (element.Value.State == MistPeerState.Disconnected) continue;

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
                MistManager.I.SendAll(MistNetMessageType.PeerData, sendData);
                // await UniTask.Delay(100);
                await UniTask.Yield();
            }
        }

        private bool CompareId(string sourceId)
        {
            var selfId = MistManager.I.MistPeerData.SelfId;
            if (String.CompareOrdinal(selfId, sourceId) < 0) return true;
            return false;
        }
        
        private void SendLeaderNotify()
        {
            MistDebug.Log($"[Leader]");
            var message = new P_LeaderNotify();
            var bytes = MemoryPackSerializer.Serialize(message);
            MistManager.I.SendAll(MistNetMessageType.LeaderNotify, bytes);
        }
    }
}