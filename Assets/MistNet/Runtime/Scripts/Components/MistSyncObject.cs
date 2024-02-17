using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace MistNet
{
    public class MistSyncObject : MonoBehaviour
    {
        public string Id { get; private set; }
        public string PrefabAddress { get; private set; }
        public string OwnerId { get; private set; }
        public bool IsOwner { get; private set; } = true;
        [HideInInspector] public MistTransform MistTransform;
        private readonly List<string> _rpcList = new();

        private void Awake()
        {
            gameObject.TryGetComponent(out MistTransform);
            RegisterRPC();
        }
        private void OnDestroy()
        {
            foreach (var rpc in _rpcList)
            {
                MistManager.I.RemoveRPC(rpc);
            }

            if (IsOwner) return;
            MistSyncManager.I.UnregisterSyncObject(this);
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

        public void RPCAll(string key, params object[] args)
        {
            var keyName = $"{Id}_{key}";
            MistManager.I.RPCAll(keyName, args);
        }
        
        public void RPC(string targetId, string key, params object[] args)
        {
            var keyName = $"{Id}_{key}";
            MistManager.I.RPC(targetId, keyName, args);
        }

        private void RegisterRPC()
        {
            // 子階層を含むすべてのComponentsを取得
            var components = gameObject.GetComponentsInChildren<Component>();

            foreach (var component in components)
            {
                // 各Componentで定義されているMethodを取得し、Attributeが付与されたメソッドを検索
                var methodsWithAttribute = component.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttributes(typeof(MistRpcAttribute), false).Length > 0);

                foreach (var methodInfo in methodsWithAttribute)
                {
                    Debug.Log($"Found method: {methodInfo.Name} in component: {component.GetType().Name}");
                    // 引数の種類に応じたDelegateを作成
                    var argTypes = methodInfo.GetParameters().Select(p => p.ParameterType).ToArray();

                    // 返り値がvoidかどうか
                    var delegateType = methodInfo.ReturnType == typeof(void)
                        ? Expression.GetActionType(argTypes)
                        : Expression.GetFuncType(argTypes.Concat(new[] { methodInfo.ReturnType }).ToArray());

                    var delegateInstance = Delegate.CreateDelegate(delegateType, component, methodInfo);
                    var keyName = $"{Id}_{delegateInstance.Method.Name}";
                    _rpcList.Add(keyName);
                    MistManager.I.AddRPC(keyName, delegateInstance);
                }
            }
        }
    }
}