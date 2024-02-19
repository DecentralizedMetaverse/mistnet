using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MemoryPack;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace MistNet
{
    public class MistSyncManager : MonoBehaviour
    {
        public static MistSyncManager I { get; private set; }
        public MistSyncObject SelfSyncObject { get; set; }                  // 自身のSyncObject
        public Dictionary<string, MistSyncObject> MySyncObjects = new();    // 自身が生成したObject一覧
        public Dictionary<string, string> OwnerIdAndObjIdDict = new();      // ownerId, objId
        private Dictionary<string, MistSyncObject> _syncObjects = new();    // objId, MistSyncObject
        
        private Dictionary<string, MistAnimator> _syncAnimators = new();    // objId, MistAnimator

        private void Awake()
        {
            I = this;
        }

        private void Start()
        {
            MistManager.I.AddRPC(MistNetMessageType.ObjectInstantiate,
                (a, b, c) => ReceiveObjectInstantiateInfo(a, b, c).Forget());
            MistManager.I.AddRPC(MistNetMessageType.Location, ReceiveLocation);
            MistManager.I.AddRPC(MistNetMessageType.Animation, ReceiveAnimation);
        }

        public void SendObjectInstantiateInfo()
        {
            var sendData = new P_ObjectInstantiate();
            foreach (var obj in MySyncObjects.Values)
            {
                sendData.ObjId = obj.Id;
                var objTransform = obj.transform;
                sendData.Position = objTransform.position;
                sendData.Rotation = objTransform.rotation.eulerAngles;
                sendData.PrefabAddress = obj.PrefabAddress;
                var data = MemoryPackSerializer.Serialize(sendData);
                MistManager.I.SendAll(MistNetMessageType.ObjectInstantiate, data);
            }
        }

        private async UniTaskVoid ReceiveObjectInstantiateInfo(byte[] data, string sourceId, string _)
        {
            var instantiateData = MemoryPackSerializer.Deserialize<P_ObjectInstantiate>(data);
            if (_syncObjects.ContainsKey(instantiateData.ObjId)) return;

            var obj = await Addressables.InstantiateAsync(instantiateData.PrefabAddress);
            obj.transform.position = instantiateData.Position;
            obj.transform.rotation = Quaternion.Euler(instantiateData.Rotation);
            var syncObject = obj.GetComponent<MistSyncObject>();
            syncObject.SetData(instantiateData.ObjId, false, instantiateData.PrefabAddress, sourceId);

            RegisterSyncObject(syncObject);
        }

        private void ReceiveLocation(byte[] data, string sourceId, string senderId)
        {
            var location = MemoryPackSerializer.Deserialize<P_Location>(data);
            var syncObject = GetSyncObject(location.ObjId);
            if (syncObject == null) return;
            syncObject.MistTransform.ReceiveLocation(location);
        }

        public void RegisterSyncObject(MistSyncObject syncObject)
        {
            if (_syncObjects.ContainsKey(syncObject.Id))
            {
                Debug.LogError($"Sync object with id {syncObject.Id} already exists!");
                return;
            }

            _syncObjects.Add(syncObject.Id, syncObject);
            if (syncObject.IsOwner)
            {
                MySyncObjects.Add(syncObject.Id, syncObject);
            }

            OwnerIdAndObjIdDict.Add(syncObject.OwnerId, syncObject.Id);

            RegisterSyncAnimator(syncObject);
        }

        public void UnregisterSyncObject(MistSyncObject syncObject)
        {
            if (!_syncObjects.ContainsKey(syncObject.Id))
            {
                Debug.LogWarning($"Sync object with id {syncObject.Id} does not exist!");
                return;
            }

            _syncObjects.Remove(syncObject.Id);
            if (MySyncObjects.ContainsKey(syncObject.Id))
            {
                MySyncObjects.Remove(syncObject.Id);
            }

            OwnerIdAndObjIdDict.Remove(syncObject.OwnerId);
            
            UnregisterSyncAnimator(syncObject);
        }

        public MistSyncObject GetSyncObject(string id)
        {
            if (!_syncObjects.ContainsKey(id))
            {
                Debug.LogWarning($"Sync object with id {id} does not exist!");
                return null;
            }

            return _syncObjects[id];
        }

        public void DestroyBySenderId(string senderId)
        {
            if (!OwnerIdAndObjIdDict.ContainsKey(senderId))
            {
                Debug.LogWarning("Already destroyed");
                return;
            }

            var objId = OwnerIdAndObjIdDict[senderId];
            Destroy(_syncObjects[objId].gameObject);
            _syncObjects.Remove(objId);

            OwnerIdAndObjIdDict.Remove(senderId);
        }

        private void RegisterSyncAnimator(MistSyncObject syncObject)
        {
            if (_syncAnimators.ContainsKey(syncObject.Id))
            {
                Debug.LogError($"Sync animator with id {syncObject.Id} already exists!");
                return;
            }

            if (!syncObject.TryGetComponent(out MistAnimator syncAnimator)) return;
            _syncAnimators.Add(syncObject.Id, syncAnimator);
        }

        private void UnregisterSyncAnimator(MistSyncObject syncObject)
        {
            if (!_syncAnimators.ContainsKey(syncObject.Id))
            {
                Debug.LogWarning($"Sync animator with id {syncObject.Id} does not exist!");
                return;
            }

            _syncAnimators.Remove(syncObject.Id);
        }
        
        private void ReceiveAnimation(byte[] data, string sourceId, string _)
        {
            var receiveData = MemoryPackSerializer.Deserialize<P_Animation>(data);
            if (!_syncAnimators.TryGetValue(receiveData.ObjId, out var syncAnimator)) return;
            syncAnimator.ReceiveAnimState(receiveData);
        }
    }
}