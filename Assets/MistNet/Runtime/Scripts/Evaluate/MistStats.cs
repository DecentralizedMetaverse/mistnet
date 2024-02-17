using System.Threading;
using Cysharp.Threading.Tasks;
using MemoryPack;
using UnityEngine;
using System;
using Debug = UnityEngine.Debug;

namespace MistNet
{
    public class MistStats : MonoBehaviour
    {
        public static MistStats I { get; private set; }
        private static readonly float IntervalPingDistanceTimeSec = 1f;
        private static readonly float IntervalSendSizeTimeSec = 1f;
        
        public int TotalSendBytes { get; set; }
        public int TotalReceiveBytes { get; set; }
        
        private CancellationTokenSource _cancellationToken;

        private void Start()
        {
            I = this;
            MistManager.I.AddRPC(MistNetMessageType.Ping, ReceivePing);
            MistManager.I.AddRPC(MistNetMessageType.Pong, ReceivePong);
            _cancellationToken = new CancellationTokenSource();
            UpdatePing(_cancellationToken.Token).Forget();
            UpdateSendSize(_cancellationToken.Token).Forget();
        }
        
        private void OnDestroy()
        {
            _cancellationToken.Cancel();
        }

        private void ReceivePing(byte[] data, string sourceId, string _)
        {
            var ping = MemoryPackSerializer.Deserialize<P_Ping>(data);
            var pong = new P_Pong
            {
                Time = ping.Time,
            };
            var sendData = MemoryPackSerializer.Serialize(pong);
            MistManager.I.Send(MistNetMessageType.Pong, sendData ,sourceId);
        }
        
        private void ReceivePong(byte[] data, string sourceId, string _)
        {
            var pong = MemoryPackSerializer.Deserialize<P_Pong>(data);
            var time = DateTime.Now.Ticks - pong.Time;
            var timeSpan = new TimeSpan(time);
            Debug.Log($"[STATS][RTT][{sourceId}] {timeSpan.Milliseconds} ms");
        }

        private async UniTask UpdatePing(CancellationToken token = default)
        {
            while (!token.IsCancellationRequested)
            {
                var ping = new P_Ping
                {
                    Time = DateTime.Now.Ticks,
                };
                var sendData = MemoryPackSerializer.Serialize(ping);
                MistManager.I.SendAll(MistNetMessageType.Ping, sendData);
                
                await UniTask.Delay(TimeSpan.FromSeconds(IntervalPingDistanceTimeSec), cancellationToken: token);
            }
        }
        
        private async UniTask UpdateSendSize(CancellationToken token = default)
        {
            while (!token.IsCancellationRequested)
            {
                // 帯域幅(bps)を計算
                var sendBps = TotalSendBytes * 8 / IntervalSendSizeTimeSec;
                Debug.Log($"[STATS][Upload] {sendBps} bps");
                
                var receiveBps = TotalReceiveBytes * 8 / IntervalSendSizeTimeSec;
                Debug.Log($"[STATS][Download] {receiveBps} bps");
                
                TotalSendBytes = 0;
                TotalReceiveBytes = 0;
                
                // 現在の接続人数を調べる
                var peers = MistPeerData.I.GetConnectedPeer;
                Debug.Log($"[STATS][Peers] {peers.Count}/{MistConfig.LimitConnection}/{MistConfig.MaxConnection}");
                
                await UniTask.Delay(TimeSpan.FromSeconds(IntervalSendSizeTimeSec), cancellationToken: token);
            }
        }
    }
}