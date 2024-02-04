using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MemoryPack;
using UnityEngine;

namespace MistNet
{
    /// <summary>
    /// 座標送信時の通信回数を減らす
    /// TODO: Listに格納する
    /// </summary>
    public class MistSendingOptimizer : MonoBehaviour
    {
        public static MistSendingOptimizer I;
        public P_Location SendLocationData;

        private readonly Dictionary<int, SendTarget> _sendTargets = new()
        {
            { 3, new() { WaitTimeSec = 0.1f, } },
            { 6, new() { WaitTimeSec = 0.25f, } },
            { 12, new() { WaitTimeSec = 0.5f, } },
            { 24, new() { WaitTimeSec = 0.75f, } },
            { 48, new() { WaitTimeSec = 1.0f, } },
            { 96, new() { WaitTimeSec = 2.0f, } },
        };

        private readonly Dictionary<string, int> _idAndDistanceDict = new();

        public void SetCategory(string id, int distance)
        {
            if (_idAndDistanceDict.TryGetValue(id, out var previousDistance))
            {
                _sendTargets[previousDistance].Ids.Remove(id);
            }

            _idAndDistanceDict[id] = distance;
            _sendTargets[distance].Ids.Add(id);
        }
        
        public void RemoveCategory(string id)
        {
            if (_idAndDistanceDict.TryGetValue(id, out var previousDistance))
            {
                _sendTargets[previousDistance].Ids.Remove(id);
            }
        }

        private class SendTarget
        {
            public float WaitTimeSec;
            public HashSet<string> Ids = new();
        }
        
        private void Awake()
        {
            I = this;
        }

        private void Start()
        {
            foreach (var distance in _sendTargets.Keys)
            {
                SendLocationWithDelay(distance).Forget();
            }
        }

        /// <summary>
        /// 座標を送信する際に間隔を空ける
        /// </summary>
        /// <param name="intervalDistance"></param>
        /// <param name="token"></param>
        private async UniTask SendLocationWithDelay(int intervalDistance, CancellationToken token = default)
        {
            var waitTimeSec = _sendTargets[intervalDistance].WaitTimeSec;
            while (!token.IsCancellationRequested)
            {
                SendLocation(_sendTargets[intervalDistance]);
                await UniTask.Delay(TimeSpan.FromSeconds(waitTimeSec), cancellationToken: token);
            }
        }

        private void SendLocation(SendTarget target)
        {
            if (SendLocationData == null) return;
            SendLocationData.Time = target.WaitTimeSec;
            var bytes = MemoryPackSerializer.Serialize(SendLocationData);
            foreach (var id in target.Ids)
            {
                MistManager.I.Send(MistNetMessageType.Location, bytes, id);
            }
        }
    }
}
