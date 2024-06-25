using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MistNet;
using Newtonsoft.Json;
using UnityEngine;

namespace MyNamespace
{
    /// <summary>
    /// 評価を行うためのクラス
    /// </summary>
    public class MistEvalLocation : MonoBehaviour
    {
        private static readonly float IntervalDistanceTimeSec = 0.95f;
        private CancellationTokenSource _cancelTokenSource = new();

        private Dictionary<string, object> _locationData = new()
        {
            { "id", "" },
            { "type", "evaluation" },
            { "location", ""},
            { "connection", ""},
        };
        private void Start()
        {
            SendLocationPeriodically(_cancelTokenSource.Token).Forget();
        }
        
        private void OnDestroy()
        {
            _cancelTokenSource.Cancel();
        }
        
        // 定期的にサーバーに座標を送信する
        private async UniTaskVoid SendLocationPeriodically(CancellationToken token = default)
        {
            while (!token.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(IntervalDistanceTimeSec), cancellationToken: token);

                if (MistSyncManager.I.SelfSyncObject == null) continue;

                _locationData["id"] = MistPeerData.I.SelfId;
                
                var connection = MistConnectionOptimizer.I != null ? MistConnectionOptimizer.I.GetConnectionInfo(): "";
                var position = MistSyncManager.I.SelfSyncObject.transform.position;
                var positionStr = $"{position.x},{position.y},{position.z}";
                _locationData["location"] = positionStr;
                _locationData["connection"] = connection;
                
                MistSignalingWebSocket.I.Ws.Send(JsonConvert.SerializeObject(_locationData));
                MistDebug.Log($"[Eval] Send location: {positionStr}");
            }
        }
    }
}