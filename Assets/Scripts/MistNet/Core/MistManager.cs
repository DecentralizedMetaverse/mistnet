using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace MistNet
{
    /// <summary>
    /// Messageの管理を行う
    /// </summary>
    public class MistManager : MonoBehaviour
    {
        private static readonly float WaitConnectingTimeSec = 3f;
        public MistConfig Config = new();
        public static MistManager I;

        public MistPeerData MistPeerData;
        public readonly MistRoutingTable RoutingTable = new();
        public Action<string> ConnectAction;

        private readonly Dictionary<MistNetMessageType, Action<byte[], string, string>>
            _onMessageDict = new(); // targetId, viaId

        public void Awake()
        {
            MistPeerData = new();
            MistPeerData.Init();
            I = this;
            Config.ReadConfig();
        }

        public void OnDestroy()
        {
            MistPeerData.Finalize();
            Config.WriteConfig();
        }

        /// <summary>
        /// TODO: viaIdを廃止していく, chunkも廃止していく
        /// </summary>
        /// <param name="type"></param>
        /// <param name="data"></param>
        /// <param name="targetId"></param>
        /// <param name="viaId">廃止</param>
        /// <param name="chunk"></param>
        public void Send(MistNetMessageType type, byte[] data, string targetId, string viaId = "", string chunk = "")
        {
            Debug.Log($"[SEND][{type.ToString()}] -> {targetId}");

            var message = new MistMessage
            {
                Id = MistPeerData.SelfId,
                Data = data,
                TargetId = targetId,
                Type = type,
                Chunk = chunk,
            };
            var sendData = MemoryPackSerializer.Serialize(message);

            if (!MistPeerData.IsConnected(targetId))
            {
                targetId = RoutingTable.Get(targetId);
                Debug.Log($"[SEND][FORWARD] {targetId} -> {message.TargetId}");
            }

            if (type == MistNetMessageType.Signaling)
            {
                Debug.Log($"[SEND][{type.ToString()}] {targetId} -> {message.TargetId}");
            }

            //if (MistPeerData.IsConnected(targetId))
            {
                var peerData = MistPeerData.GetAllPeer[targetId];
                peerData.Peer.Send(sendData).Forget();
            }
            // else
            // {
            //     Debug.LogError($"[Send] peerId: {targetId} is not found");
            // }
        }

        public void SendAll(MistNetMessageType type, byte[] data, string chunk = "")
        {
            var message = new MistMessage
            {
                Id = MistPeerData.SelfId,
                Data = data,
                Type = type,
                Chunk = chunk,
            };

            foreach (var peerData in MistPeerData.GetConnectedPeer)
            {
                Debug.Log($"[SEND][{peerData.Id}] {type.ToString()}");
                message.TargetId = peerData.Id;
                var sendData = MemoryPackSerializer.Serialize(message);
                peerData.Peer.Send(sendData).Forget();
            }
        }
        
        public void Register(MistNetMessageType messageType, Action<byte[], string, string> function)
        {
            _onMessageDict.Add(messageType, function);
        }

        public void OnMessage(byte[] data, string senderId)
        {
            var message = MemoryPackSerializer.Deserialize<MistMessage>(data);
            Debug.Log($"[RECV][{message.Type.ToString()}] {message.Id} -> {message.TargetId}");

            // RoutingTable.Add(message.Id, id);

            if (IsMessageForSelf(message))
            {
                Debug.Log($"[Debug][RECV][SELF] {message.Type.ToString()}");
                ProcessMessageForSelf(message, senderId);
                return;
            }

            var targetId = message.TargetId;
            if (!MistPeerData.IsConnected(message.TargetId))
            {
                targetId = RoutingTable.Get(message.TargetId);
            }

            if (!string.IsNullOrEmpty(targetId))
            {
                var peer = MistPeerData.GetPeer(targetId);
                if (peer == null) return;
                peer.Send(data).Forget();
                Debug.Log($"[RECV][SEND][FORWARD][{message.Type.ToString()}] {message.Id} -> {MistPeerData.I.SelfId} -> {message.TargetId}");
                return;
            }
        }

        #region OnMessage

        private bool IsMessageForSelf(MistMessage message)
        {
            return message.TargetId == MistPeerData.SelfId;
            // ||
            //        (
            //            string.IsNullOrEmpty(message.TargetId) &&
            //            MistOptimizationManager.I.Data.IsSameChunkWithSelf(message.Chunk)
            //        );
        }

        private void ProcessMessageForSelf(MistMessage message, string senderId)
        {
            MistManager.I.RoutingTable.Add(message.Id, senderId);
            _onMessageDict[message.Type].DynamicInvoke(message.Data, message.Id, senderId);
        }
        #endregion

        public async UniTaskVoid Connect(string id)
        {
            ConnectAction.Invoke(id);
            MistPeerData.GetPeerData(id).State = MistPeerState.Connecting;
            await UniTask.Delay(TimeSpan.FromSeconds(WaitConnectingTimeSec));
            if (MistPeerData.GetPeerData(id).State == MistPeerState.Connecting)
            {
                Debug.Log($"[Connect] {id} is not connected");
                MistPeerData.GetPeerData(id).State = MistPeerState.Disconnected;
            }
        }

        public void OnConnected(string id)
        {
            Debug.Log($"[Connected] {id}");

            // InstantiateしたObject情報の送信
            MistPeerData.I.GetPeerData(id).State = MistPeerState.Connected;
            MistSyncManager.I.SendObjectInstantiateInfo();
            MistOptimizationManager.I.OnConnected(id);
        }

        public void OnDisconnected(string id)
        {
            Debug.Log($"[Disconnected] {id}");
            // MistPeerData.Dict.Remove(id);
            MistSyncManager.I.DestroyBySenderId(id);
            MistOptimizationManager.I.OnDisconnected(id);

            MistPeerData.I.GetPeerData(id).State = MistPeerState.Disconnected;
            MistPeerData.I.GetAllPeer.Remove(id);
        }

        public void Disconnect(string id)
        {
            var peer = MistPeerData.GetPeer(id);
            peer.Close();
            OnDisconnected(id);
        }

        /// <summary>
        /// TODO: prefabAddress.PrimaryKeyがAddressを表しているかどうかの確認が必要
        /// </summary>
        /// <param name="prefabAddress"></param>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        /// <returns></returns>
        public async UniTask<GameObject> InstantiateAsync(IResourceLocation prefabAddress, Vector3 position,
            Quaternion rotation)
        {
            var obj = await Addressables.InstantiateAsync(prefabAddress, position, rotation);
            InstantiateObject(prefabAddress.PrimaryKey, position, rotation, obj);
            return obj;
        }

        public async UniTask<GameObject> InstantiateAsync(string prefabAddress, Vector3 position, Quaternion rotation)
        {
            var obj = await Addressables.InstantiateAsync(prefabAddress, position, rotation);
            InstantiateObject(prefabAddress, position, rotation, obj);
            return obj;
        }

        private void InstantiateObject(string prefabAddress, Vector3 position, Quaternion rotation, GameObject obj)
        {
            var syncObject = obj.GetComponent<MistSyncObject>();
            var objId = Guid.NewGuid().ToString("N");
            syncObject.SetData(objId, true, prefabAddress, MistPeerData.SelfId);

            MistSyncManager.I.RegisterSyncObject(syncObject);

            var sendData = new P_ObjectInstantiate()
            {
                ObjId = objId,
                Position = position,
                Rotation = rotation.eulerAngles,
                PrefabAddress = prefabAddress,
            };

            var bytes = MemoryPackSerializer.Serialize(sendData);
            SendAll(MistNetMessageType.ObjectInstantiate, bytes);
        }
    }
}