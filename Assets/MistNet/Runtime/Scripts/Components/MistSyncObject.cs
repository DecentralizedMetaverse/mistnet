using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MistNet
{
    public class MistSyncObject : MonoBehaviour
    {
        private const float WaitTimeSec = 0.5f;

        public string Id { get; private set; }
        public string PrefabAddress { get; private set; }
        public string OwnerId { get; private set; }
        public bool IsOwner { get; private set; } = true;
        [HideInInspector] public MistTransform MistTransform;

        private readonly List<string> _rpcList = new();
        private readonly List<(Component, PropertyInfo)> _propertyList = new();
        private readonly Dictionary<string, object> _propertyValueDict = new();
        private CancellationTokenSource _tokenSource;

        private void Awake()
        {
            gameObject.TryGetComponent(out MistTransform);
        }

        private void Start()
        {
            _tokenSource = new();
            Register();
            if (IsOwner) WatchPropertiesAsync(_tokenSource.Token).Forget();
        }

        private void OnDestroy()
        {
            _tokenSource.Cancel();

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

        public void RPC(string targetId, string key, params object[] args)
        {
            var keyName = $"{Id}_{key}";
            MistManager.I.RPC(targetId, keyName, args);
        }

        public void RPCAll(string key, params object[] args)
        {
            var keyName = $"{Id}_{key}";
            MistManager.I.RPCAll(keyName, args);
        }

        public void RPCAllWithSelf(string key, params object[] args)
        {
            var keyName = $"{Id}_{key}";
            MistManager.I.RPCAllWithSelf(keyName, args);
        }

        private void Register()
        {
            // 子階層を含むすべてのComponentsを取得
            var components = gameObject.GetComponentsInChildren<Component>();

            foreach (var component in components)
            {
                // 各Componentで定義されているMethodを取得し、Attributeが付与されたメソッドを検索
                var methodsWithAttribute = component.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttributes(typeof(MistRpcAttribute), false).Length > 0);

                RegisterRPCMethods(methodsWithAttribute, component);


                var propertyInfos = component.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(prop => Attribute.IsDefined(prop, typeof(MistSyncAttribute))).ToList();

                RegisterSyncProperties(propertyInfos, component);
            }
        }

        /// <summary>
        /// 他のPeerからのpropertyの変更を受け取るための処理
        /// </summary>
        /// <param name="propertyInfos"></param>
        /// <param name="component"></param>
        private void RegisterSyncProperties(List<PropertyInfo> propertyInfos, Component component)
        {
            foreach (var property in propertyInfos)
            {
                _propertyList.Add((component, property));

                var keyName = $"{Id}_{property.Name}";
                var delegateType = typeof(Action<>).MakeGenericType(property.PropertyType);

                // MistSyncAttributeからOnChangedメソッド名を取得
                var mistSyncAttr = (MistSyncAttribute)Attribute.GetCustomAttribute(property, typeof(MistSyncAttribute));
                var onChangedMethodName = mistSyncAttr?.OnChanged;

                MethodInfo onChangedMethodInfo = null;
                if (!string.IsNullOrEmpty(onChangedMethodName))
                {
                    onChangedMethodInfo = component.GetType().GetMethod(onChangedMethodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }

                var originalSetMethod = property.SetMethod;

                // Wrapperを作成
                Action<object> wrapper = value =>
                {
                    originalSetMethod.Invoke(component, new[] { value });
                    onChangedMethodInfo?.Invoke(component, null);
                };

                // 登録
                var wrapperDelegate = Delegate.CreateDelegate(delegateType, wrapper.Target, wrapper.Method);

                _rpcList.Add(keyName);
                MistManager.I.AddRPC(keyName, wrapperDelegate);
            }
        }

        /// <summary>
        /// 他のPeerからのRPCを受け取るための処理
        /// </summary>
        /// <param name="methodsWithAttribute"></param>
        /// <param name="component"></param>
        private void RegisterRPCMethods(IEnumerable<MethodInfo> methodsWithAttribute, Component component)
        {
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

        private async UniTask WatchPropertiesAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var (component, property) in _propertyList)
                {
                    // 保存されたプロパティ情報を使用して値を取得し、ログに出力
                    var value = property.GetValue(component);
                    var keyName = $"{Id}_{property.Name}";

                    if (!_propertyValueDict.TryGetValue(keyName, out var previousValue))
                    {
                        if (value != null) _propertyValueDict.Add(keyName, value);
                        continue;
                    }

                    if (previousValue.Equals(value)) continue;

                    _propertyValueDict[keyName] = value;
                    Debug.Log($"Property: {property.Name}, Value: {value}");
                    MistManager.I.RPCAll(keyName, value);
                }

                await UniTask.Delay(TimeSpan.FromSeconds(WaitTimeSec), cancellationToken: token);
            }
        }
    }
}