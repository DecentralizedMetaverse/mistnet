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
        public MistSyncObject SelfSyncObject { get; set; }                           // 自身のSyncObject

        private readonly Dictionary<string, MistSyncObject> _syncObjects = new();    // objId, MistSyncObject
        private readonly Dictionary<string, MistAnimator> _syncAnimators = new();    // objId, MistAnimator

        // ユーザーが退出した際のGameObjectの削除に使用している Instantiateで生成されたObjectに限る
        public readonly Dictionary<string, List<string>> OwnerIdAndObjIdDict = new();  // ownerId, objId　
        private readonly Dictionary<string, MistSyncObject> _mySyncObjects = new();    // 自身が生成したObject一覧

        private void Awake()
        {
            I = this;
        }

        private void Start()
        {
            MistManager.I.AddRPC(MistNetMessageType.ObjectInstantiate,
                (a, b) => ReceiveObjectInstantiateInfo(a, b).Forget());
            MistManager.I.AddRPC(MistNetMessageType.Location, ReceiveLocation);
            MistManager.I.AddRPC(MistNetMessageType.Animation, ReceiveAnimation);
            MistManager.I.AddRPC(MistNetMessageType.PropertyRequest, (_, sourceId) => SendAllProperties(sourceId));
        }

        public void SendObjectInstantiateInfo(string id)
        {
            var sendData = new P_ObjectInstantiate();
            foreach (var obj in _mySyncObjects.Values)
            {
                sendData.ObjId = obj.Id;
                var objTransform = obj.transform;
                sendData.Position = objTransform.position;
                sendData.Rotation = objTransform.rotation.eulerAngles;
                sendData.PrefabAddress = obj.PrefabAddress;
                var data = MemoryPackSerializer.Serialize(sendData);
                MistManager.I.Send(MistNetMessageType.ObjectInstantiate, data, id);
            }
        }

        private async UniTaskVoid ReceiveObjectInstantiateInfo(byte[] data, string sourceId)
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

        private void ReceiveLocation(byte[] data, string sourceId)
        {
            var location = MemoryPackSerializer.Deserialize<P_Location>(data);
            var syncObject = GetSyncObject(location.ObjId);
            if (syncObject == null) return;
            syncObject.MistTransform.ReceiveLocation(location);
        }

        public void RegisterSyncObject(MistSyncObject syncObject)
        {
            if (!_syncObjects.TryAdd(syncObject.Id, syncObject))
            {
                MistDebug.LogError($"Sync object with id {syncObject.Id} already exists!");
                return;
            }

            if (syncObject.IsOwner)
            {
                // 最初のGameObjectは、接続先最適化に使用するため、PlayerObjectであることを設定
                if(_mySyncObjects.Count == 0) syncObject.IsPlayerObject = true;
                
                _mySyncObjects.Add(syncObject.Id, syncObject);
            }
            else if (syncObject.IsGlobalObject)
            {
                // 誰のものでもないGlobalObjectの場合
                RegisterSyncAnimator(syncObject);
                return; // OwnerIdAndObjIdDictに登録する必要がないのでここで中断
            }
            else
            {
                // 自身以外のSyncObjectの登録
                var sendData = new P_PropertyRequest();
                var bytes = MemoryPackSerializer.Serialize(sendData);
                MistManager.I.Send(MistNetMessageType.PropertyRequest, bytes, syncObject.OwnerId);
            }

            // OwnerIdAndObjIdDictに登録 自動削除で使用する
            if (!OwnerIdAndObjIdDict.ContainsKey(syncObject.OwnerId))
            {
                OwnerIdAndObjIdDict[syncObject.OwnerId] = new List<string>();
            }
            OwnerIdAndObjIdDict[syncObject.OwnerId].Add(syncObject.Id);

            RegisterSyncAnimator(syncObject);
        }

        private void SendAllProperties(string id)
        {
            foreach (var obj in _mySyncObjects.Values)
            {
                obj.SendAllProperties(id);
            }
        }

        public void UnregisterSyncObject(MistSyncObject syncObject)
        {
            if (!_syncObjects.ContainsKey(syncObject.Id))
            {
                MistDebug.LogWarning($"Sync object with id {syncObject.Id} does not exist!");
                return;
            }

            _syncObjects.Remove(syncObject.Id);
            if (_mySyncObjects.ContainsKey(syncObject.Id))
            {
                _mySyncObjects.Remove(syncObject.Id);
            }

            OwnerIdAndObjIdDict.Remove(syncObject.OwnerId);
            
            UnregisterSyncAnimator(syncObject);
        }

        public MistSyncObject GetSyncObject(string id)
        {
            if (!_syncObjects.ContainsKey(id))
            {
                MistDebug.LogWarning($"Sync object with id {id} does not exist!");
                return null;
            }

            return _syncObjects[id];
        }

        public void DestroyBySenderId(string senderId)
        {
            if (!OwnerIdAndObjIdDict.ContainsKey(senderId))
            {
                MistDebug.LogWarning("Already destroyed");
                return;
            }

            var objId = OwnerIdAndObjIdDict[senderId];
            foreach (var id in objId)
            {
                Destroy(_syncObjects[id].gameObject);
                _syncObjects.Remove(id);
            }

            OwnerIdAndObjIdDict.Remove(senderId);
        }

        private void RegisterSyncAnimator(MistSyncObject syncObject)
        {
            if (_syncAnimators.ContainsKey(syncObject.Id))
            {
                MistDebug.LogError($"Sync animator with id {syncObject.Id} already exists!");
                return;
            }

            if (!syncObject.TryGetComponent(out MistAnimator syncAnimator)) return;
            _syncAnimators.Add(syncObject.Id, syncAnimator);
        }

        private void UnregisterSyncAnimator(MistSyncObject syncObject)
        {
            if (!_syncAnimators.ContainsKey(syncObject.Id))
            {
                MistDebug.LogWarning($"Sync animator with id {syncObject.Id} does not exist!");
                return;
            }

            _syncAnimators.Remove(syncObject.Id);
        }
        
        private void ReceiveAnimation(byte[] data, string sourceId)
        {
            var receiveData = MemoryPackSerializer.Deserialize<P_Animation>(data);
            if (!_syncAnimators.TryGetValue(receiveData.ObjId, out var syncAnimator)) return;
            syncAnimator.ReceiveAnimState(receiveData);
        }
    }
}
