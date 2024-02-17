using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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
            gameObject.TryGetComponent(out MistTransform);
            RegisterRPC();
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

        private void RegisterRPC()
        {
            // 対象のGameObjectにアタッチされているすべてのコンポーネントを取得
            var components = gameObject.GetComponents<Component>();

            foreach (var component in components)
            {
                // 各コンポーネントで定義されているメソッドを取得し、MyCustomAttributeが付与されたメソッドを検索
                var methodsWithAttribute = component.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttributes(typeof(MistRpcAttribute), false).Length > 0);

                foreach (var methodInfo in methodsWithAttribute)
                {
                    Debug.Log($"Found method: {methodInfo.Name} in component: {component.GetType().Name}");
                    // 引数の種類に応じたDelegateを作成
                    var argTypes = methodInfo.GetParameters().Select(p => p.ParameterType).ToArray();

                    // メソッドがvoidかどうかをチェックします
                    var delegateType = methodInfo.ReturnType == typeof(void)
                        ? Expression.GetActionType(argTypes)
                        : Expression.GetFuncType(argTypes.Concat(new[] { methodInfo.ReturnType }).ToArray());
                    
                    var delegateInstance = Delegate.CreateDelegate(delegateType, component, methodInfo);
                    MistManager.I.AddRPC(delegateInstance);
                }
            }
        }
    }
}