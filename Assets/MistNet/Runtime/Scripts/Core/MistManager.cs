using MemoryPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
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
        public readonly MistConfig Config = new();
        public static MistManager I;

        public MistPeerData MistPeerData;
        public readonly MistRoutingTable RoutingTable = new();
        public Action<string> ConnectAction;

        private readonly Dictionary<MistNetMessageType, Action<byte[], string, string>>
            _onMessageDict = new(); // targetId, viaId

        private static Dictionary<string, Delegate> _functions = new();
        private Dictionary<string, int> _functionArgsLength = new();


        public void Awake()
        {
            MistPeerData = new();
            MistPeerData.Init();
            I = this;
            Config.ReadConfig();
        }

        private void Start()
        {
            AddRPC(MistNetMessageType.RPC, OnRPC);
        }

        public void OnDestroy()
        {
            MistPeerData.AllClose();
            Config.WriteConfig();
        }

        /// <summary>
        /// TODO: viaIdを廃止していく, chunkも廃止していく
        /// </summary>
        /// <param name="type"></param>
        /// <param name="data"></param>
        /// <param name="targetId"></param>
        public void Send(MistNetMessageType type, byte[] data, string targetId)
        {
            Debug.Log($"[SEND][{type.ToString()}] -> {targetId}");

            var message = new MistMessage
            {
                Id = MistPeerData.SelfId,
                Data = data,
                TargetId = targetId,
                Type = type,
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

            if (MistPeerData.IsConnected(targetId))
            {
                var peerData = MistPeerData.GetAllPeer[targetId];
                peerData.Peer.Send(sendData).Forget();
            }
        }

        public void SendAll(MistNetMessageType type, byte[] data)
        {
            var message = new MistMessage
            {
                Id = MistPeerData.SelfId,
                Data = data,
                Type = type,
            };

            foreach (var peerData in MistPeerData.GetConnectedPeer)
            {
                Debug.Log($"[SEND][{peerData.Id}] {type.ToString()}");
                message.TargetId = peerData.Id;
                var sendData = MemoryPackSerializer.Serialize(message);
                peerData.Peer.Send(sendData).Forget();
            }
        }

        public void AddRPC(MistNetMessageType messageType, Action<byte[], string, string> function)
        {
            _onMessageDict.Add(messageType, function);
        }

        public void AddRPC(string key, Delegate function)
        {
            _functions.Add(key, function);
            _functionArgsLength.Add(key, function.GetMethodInfo().GetParameters().Length);
        }
        
        public void RemoveRPC(string key)
        {
            _functions.Remove(key);
            _functionArgsLength.Remove(key);
        }

        public void RPC(string targetId, string key, params object[] args)
        {
            var argsString = string.Join(",", args);
            var sendData = new P_RPC
            {
                Method = key,
                Args = argsString,
            };
            var bytes = MemoryPackSerializer.Serialize(sendData);
            Send(MistNetMessageType.RPC, bytes, targetId);
        }
        
        public void RPCAll(string key, params object[] args)
        {
            var argsString = string.Join(",", args);
            var sendData = new P_RPC
            {
                Method = key,
                Args = argsString,
            };
            var bytes = MemoryPackSerializer.Serialize(sendData);
            SendAll(MistNetMessageType.RPC, bytes);
        }

        public void RPCAllWithSelf(string key, params object[] args)
        {
            RPCAll(key, args);
            _functions[key].DynamicInvoke(args);
        }

        private void OnRPC(byte[] data, string sourceId, string senderId)
        {
            var message = MemoryPackSerializer.Deserialize<P_RPC>(data);
            var args = ConvertStringToObjects(message.Args);
            var argsLength = _functionArgsLength[message.Method];
            
            if (args.Count != argsLength)
            {
                args.Add(new MessageInfo
                {
                    SourceId = sourceId,
                    SenderId = senderId,
                });
            }

            _functions[message.Method].DynamicInvoke(args.ToArray());
        }

        private List<object> ConvertStringToObjects(string input)
        {
            var objects = new List<object>();
            var parts = input.Split(',');

            foreach (var part in parts)
            {
                if (int.TryParse(part, out var intValue))
                {
                    objects.Add(intValue);
                }
                else if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                {
                    objects.Add(floatValue);
                }
                else
                {
                    objects.Add(part);
                }
            }

            return objects;
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
                Debug.Log(
                    $"[RECV][SEND][FORWARD][{message.Type.ToString()}] {message.Id} -> {MistPeerData.I.SelfId} -> {message.TargetId}");
            }
        }

        private bool IsMessageForSelf(MistMessage message)
        {
            return message.TargetId == MistPeerData.SelfId;
        }

        private void ProcessMessageForSelf(MistMessage message, string senderId)
        {
            MistManager.I.RoutingTable.Add(message.Id, senderId);
            _onMessageDict[message.Type].DynamicInvoke(message.Data, message.Id, senderId);
        }

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