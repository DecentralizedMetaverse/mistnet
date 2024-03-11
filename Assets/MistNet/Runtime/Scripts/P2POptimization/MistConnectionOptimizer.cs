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
    /// 接続先を最適化するクラス
    /// </summary>
    public class MistConnectionOptimizer : MonoBehaviour
    {
        private const float IntervalSendTableTimeSec = 1.5f;
        private const float RemoveDisconnectTimeSec = 10f;
        private const int BlockConnectIntervalTimeSec = 10;
        private const float IntervalLogPeerTableTimeSec = 5f; // ピアテーブルをログ出力する間隔

        public static MistConnectionOptimizer I { get; private set; }
        private CancellationTokenSource _cancellationTokenSource;

        private void Awake()
        {
            I = this;
        }

        private void Start()
        {
            _cancellationTokenSource = new CancellationTokenSource();

            // RPCの登録
            RegisterRPCHandlers();

            // 定期的にピア情報を送信
            SendPeerTablePeriodically(_cancellationTokenSource.Token).Forget();

            // 接続の最適化を定期的に実行
            OptimizeConnections(_cancellationTokenSource.Token).Forget();
        }

        private void OnDestroy()
        {
            _cancellationTokenSource.Cancel();
        }

        /// <summary>
        /// RPCハンドラを登録する
        /// </summary>
        private void RegisterRPCHandlers()
        {
            MistManager.I.AddRPC(MistNetMessageType.PeerData, OnPeerTableResponse);
        }

        /// <summary>
        /// ピア情報を受信したときの処理
        /// </summary>
        private void OnPeerTableResponse(byte[] data, string sourceId)
        {
            var message = MemoryPackSerializer.Deserialize<P_PeerData>(data);
            MistManager.I.RoutingTable.Add(message.Id, sourceId);

            if (string.IsNullOrEmpty(message.Id) || message.Id == MistManager.I.MistPeerData.SelfId)
            {
                return;
            }

            MistPeerData.I.UpdatePeerData(message.Id, message);
        }

        /// <summary>
        /// 切断されたときの処理
        /// </summary>
        public void OnDisconnected(string id)
        {
            MistPeerData.I.SetState(id, MistPeerState.Disconnected);
            MistPeerData.I.GetPeerData(id).BlockConnectIntervalTime = BlockConnectIntervalTimeSec;
            UpdateBlockConnectIntervalTime(id, _cancellationTokenSource.Token).Forget();
        }

        /// <summary>
        /// ブロック時間を更新する
        /// </summary>
        private async UniTaskVoid UpdateBlockConnectIntervalTime(string id, CancellationToken token)
        {
            var peerData = MistPeerData.I.GetPeerData(id);
            while (peerData.BlockConnectIntervalTime > 0)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
                peerData.BlockConnectIntervalTime--;
            }
        }

        /// <summary>
        /// 周辺のピア情報を取得する
        /// </summary>
        private List<MistPeerDataElement> GetNearbyPeers()
        {
            var selfPosition = MistSyncManager.I.SelfSyncObject.transform.position;
            var nearbyPeers = MistPeerData.I.GetAllPeer.Values
                .OrderBy(x => Vector3.Distance(selfPosition, x.Position))
                .Take(MistConfig.LimitConnection)
                .ToList();

            return nearbyPeers;
        }

        /// <summary>
        /// 接続を最適化する
        /// </summary>
        private async UniTaskVoid OptimizeConnections(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
                if (MistSyncManager.I.SelfSyncObject == null) continue;
        
                var nearbyPeers = GetNearbyPeers();
                LogPeerTable(nearbyPeers);

                var connectRequests = new List<string>();
                var disconnectRequests = new List<string>();

                // nearbyPeers以外のピアを切断リクエストに追加
                var allPeers = MistPeerData.I.GetAllPeer.Values;
                foreach (var peer in allPeers)
                {
                    if (nearbyPeers.Contains(peer)) continue;
                    var peerData = MistPeerData.I.GetPeerData(peer.Id);
        
                    // 相手の現在の接続数が3より大きい場合のみ切断リクエストに追加
                    if (peerData.CurrentConnectNum > MistConfig.MinConnection)
                    {
                        disconnectRequests.Add(peer.Id);
                    }
                }

                foreach (var peer in nearbyPeers)
                {
                    var peerData = MistPeerData.I.GetPeerData(peer.Id);

                    if (peerData.State == MistPeerState.Disconnected &&
                        peerData.CurrentConnectNum < peerData.MaxConnectNum &&
                        peerData.BlockConnectIntervalTime <= 0)
                    {
                        connectRequests.Add(peer.Id);
                    }
                }

                // 接続リクエストを送信
                foreach (var id in connectRequests)
                {
                    SendConnectRequest(id);
                }

                // 切断リクエストを送信
                foreach (var id in disconnectRequests)
                {
                    SendDisconnectRequest(id);
                }

            }
        }

        /// <summary>
        /// 切断リクエストを送信する
        /// </summary>
        private void SendDisconnectRequest(string id)
        {
            if (CompareId(id)) return;
            MistManager.I.Disconnect(id);
        }

        /// <summary>
        /// 接続リクエストを送信する
        /// </summary>
        private void SendConnectRequest(string id)
        {
            if (id == MistManager.I.MistPeerData.SelfId) return;
            MistManager.I.Connect(id).Forget();
        }

        /// <summary>
        /// 定期的にピア情報を送信する
        /// </summary>
        private async UniTaskVoid SendPeerTablePeriodically(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(IntervalSendTableTimeSec), cancellationToken: token);
                SendPeerData().Forget();
            }
        }

        /// <summary>
        /// ピア情報を送信する
        /// </summary>
        private async UniTaskVoid SendPeerData()
        {
            var sendList = new List<byte[]>();

            // 自身の情報を追加
            var selfData = CreateSelfPeerData();
            var selfBytes = MemoryPackSerializer.Serialize(selfData);
            sendList.Add(selfBytes);

            // 周辺のピアの情報を追加
            var nearbyPeers = GetNearbyPeers();
            foreach (var element in nearbyPeers)
            {
                if (IsValidPeer(element))
                {
                    var sendData = CreatePeerData(element);
                    var bytes = MemoryPackSerializer.Serialize(sendData);
                    sendList.Add(bytes);
                }
            }

            // ピア情報を送信
            foreach (var sendData in sendList)
            {
                MistManager.I.SendAll(MistNetMessageType.PeerData, sendData);
                await UniTask.Yield();
            }
        }

        /// <summary>
        /// 自身のピア情報を作成する
        /// </summary>
        private P_PeerData CreateSelfPeerData()
        {
            return new P_PeerData
            {
                Id = MistManager.I.MistPeerData.SelfId,
                Position = MistSyncManager.I.SelfSyncObject.transform.position,
                CurrentConnectNum = MistManager.I.MistPeerData.GetConnectedPeer.Count,
                LimitConnectNum = MistConfig.LimitConnection,
                MaxConnectNum = MistConfig.MaxConnection,
            };
        }

        /// <summary>
        /// ピア情報を作成する
        /// </summary>
        private P_PeerData CreatePeerData(MistPeerDataElement element)
        {
            return new P_PeerData
            {
                Id = element.Id,
                Position = element.Position,
                CurrentConnectNum = element.CurrentConnectNum,
                MinConnectNum = element.MinConnectNum,
                LimitConnectNum = element.LimitConnectNum,
                MaxConnectNum = element.MaxConnectNum,
            };
        }

        /// <summary>
        /// 有効なピアかどうかを判定する
        /// </summary>
        private bool IsValidPeer(MistPeerDataElement element)
        {
            return !string.IsNullOrEmpty(element.Id) &&
                   element.MaxConnectNum != 0 &&
                   element.State != MistPeerState.Disconnected;
        }

        /// <summary>
        /// IDを比較する
        /// </summary>
        private bool CompareId(string sourceId)
        {
            var selfId = MistManager.I.MistPeerData.SelfId;
            return string.CompareOrdinal(selfId, sourceId) < 0;
        }

        /// <summary>
        /// ピアテーブルをログ出力する
        /// </summary>
        private void LogPeerTable(List<MistPeerDataElement> nearbyPeers)
        {
            var logMessage = "[Info] Peer Table:\n";

            foreach (var peer in nearbyPeers)
            {
                logMessage += $"[{peer.Id}] {peer.State} {peer.CurrentConnectNum}/{peer.MaxConnectNum} {peer.Position}\n";
            }

            MistDebug.Log(logMessage);
        }
    }
}