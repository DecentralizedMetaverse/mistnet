using UnityEngine;

namespace MistNet
{
    [RequireComponent(typeof(MistSyncObject))]
    public class MistBehaviour : MonoBehaviour
    {
        protected MistSyncObject SyncObject { get; private set; }
        // 監視対象のプロパティ情報を格納する配列

        protected virtual void Awake()
        {
            SyncObject = GetComponent<MistSyncObject>();
        }
    }
}