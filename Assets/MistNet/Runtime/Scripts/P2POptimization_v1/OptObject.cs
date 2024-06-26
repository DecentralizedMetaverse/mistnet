using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MistNet.Runtime.Scripts.Utils;
using UnityEngine;

namespace MistNet.Opt
{
    /// <summary>
    /// このComponentを持つGameObjectはP2P最適化の対象となる
    /// </summary>
    [RequireComponent(typeof(MistSyncObject))]
    public class OptObject : MonoBehaviour
    {
        private const float UpdateTimeSec = 3.0f;
        private MistSyncObject _syncObject;
        private (int, int, int) _chunk;

        private void Start()
        {
            _syncObject = GetComponent<MistSyncObject>();
            if (_syncObject.IsOwner)
            {
                UpdateChunk(this.GetCancellationTokenOnDestroy()).Forget();
            }
        }

        private async UniTask UpdateChunk(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(UpdateTimeSec), cancellationToken: token);

                // Chunkの計算
                var (x, y, z) = MistUtils.GetChunk(transform.position);
                if (_chunk == (x, y, z)) continue;
                _chunk = (x, y, z);
                OptLayer.I.OnChangedChunk(_chunk);
            }
        }
    }
}