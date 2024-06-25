using MistNet;
using MistNet.Runtime.Scripts.Utils;
using UnityEngine;

/// <summary>
/// このComponentを持つGameObjectはP2P最適化の対象となる
/// </summary>
[RequireComponent(typeof(MistSyncObject))]
public class OptObject : MonoBehaviour
{
    private MistSyncObject _syncObject;

    private void Start()
    {
        _syncObject = GetComponent<MistSyncObject>();
    }

    private void Update()
    {
        if (!_syncObject.IsOwner) return;

        // Chunkの計算
        var (x, y ,z) = MistUtils.GetChunk(transform.position);
    }
}
