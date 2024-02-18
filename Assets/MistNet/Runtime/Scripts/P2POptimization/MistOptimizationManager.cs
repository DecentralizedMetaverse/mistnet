using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using System.Threading;

namespace MistNet
{
    public class MistOptimizationManager : MonoBehaviour
    {
        private static readonly float IntervalDistanceTimeSec = 1f;
        public static MistOptimizationManager I { get; private set; }
        public MistOptimizationData Data;
        private CancellationTokenSource _cancelTokenSource;

        private void Awake()
        {
            I = this;
            Data = new MistOptimizationData();
        }

        private void Start()
        {
            _cancelTokenSource = new();
            UpdateCalculateDistance(_cancelTokenSource.Token).Forget();
        }

        public void OnConnected(string id)
        {
            Data.AddPeer(id);
        }
        
        public void OnDisconnected(string id)
        {
            Data.RemovePeer(id);
        }
        
        /// <summary>
        /// 一定時間ごとに距離を計算する
        /// Userとの近さに応じて、送信頻度を変更するためのもの
        /// </summary>
        /// <param name="token"></param>
        private async UniTaskVoid UpdateCalculateDistance(CancellationToken token = default)
        {
            while (!token.IsCancellationRequested)
            {
                Data.UpdateDistance();
                await UniTask.Delay(TimeSpan.FromSeconds(IntervalDistanceTimeSec), cancellationToken: token);
            }
        }
    }
}