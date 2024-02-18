using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace MistNet
{
    [RequireComponent(typeof(MistSyncObject))]
    public class MistBehaviour : MonoBehaviour
    {
        public MistSyncObject MistSyncObject;
        
        protected virtual void Awake()
        {
            MistSyncObject = GetComponent<MistSyncObject>();
        }
    }
}