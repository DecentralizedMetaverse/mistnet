using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MistNet
{
    public class MistSyncObject : MonoBehaviour
    {
        public string Id;
        public string PrefabAddress;
        public string OwnerId { get; private set; }
        public bool IsOwner { get; private set; } = true;
        [HideInInspector] public MistTransform MistTransform;
        [HideInInspector] public Chunk Chunk;
        
        private void Awake()
        {
            // MistSyncManager.I.RegisterSyncObject(this);
            gameObject.TryGetComponent(out MistTransform);
        }

        public void SetData(string id, bool isOwner, string prefabAddress, string ownerId)
        {
            Id = id;
            IsOwner = isOwner;
            PrefabAddress = prefabAddress;
            OwnerId = ownerId;
            gameObject.TryGetComponent(out MistTransform);
            
            if (IsOwner) MistSyncManager.I.SelfSyncObject = this;
        }

        private void OnDestroy()
        {
            MistSyncManager.I.UnregisterSyncObject(this);
        }

        private void Update()
        {
            if (!IsOwner) return;
            
            // 座標からChunkの更新
            Chunk.Update(transform.position);
            // if (Chunk.Update(transform.position))
            // {
            //     MistConnectionOptimizer.I.OnChangedChunk(Chunk);
            // }
        }
    }
}