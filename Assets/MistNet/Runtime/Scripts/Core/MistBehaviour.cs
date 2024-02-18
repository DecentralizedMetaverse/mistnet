using UnityEngine;

namespace MistNet
{
    [RequireComponent(typeof(MistSyncObject))]
    public class MistBehaviour : MonoBehaviour
    {
        public MistSyncObject MistSyncObject { get; private set; }
        
        protected virtual void Awake()
        {
            MistSyncObject = GetComponent<MistSyncObject>();
        }
    }
}