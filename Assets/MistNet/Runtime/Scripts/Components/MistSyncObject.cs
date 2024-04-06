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
        public string Id { get; private set; }
        public string PrefabAddress { get; private set; }
        public string OwnerId { get; private set; }
        public bool IsOwner { get; private set; } = true;
        public bool IsPlayerObject { get; set; }
        [HideInInspector] public MistTransform MistTransform;
        [SerializeField] private float syncIntervalSeconds = 0.5f;

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

        public void SendAllProperties(string id)
        {
            foreach (var (component, property) in _propertyList)
            {
                var keyName = $"{Id}_{property.Name}";
                var value = property.GetValue(component);
                MistManager.I.RPC(id, keyName, value);
            }
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


                // 各Componentで定義されているPropertyを取得し、Attributeが付与されたプロパティを検索
                var propertyInfos = component.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(prop => Attribute.IsDefined(prop, typeof(MistSyncAttribute))).ToList();

                RegisterSyncProperties(propertyInfos, component);

                // 各Componentで定義されているInterfaceを取得
                var interfaces = component.GetType().GetInterfaces();
                RegisterCallback(interfaces, component);
            }
        }

        private static void RegisterCallback(Type[] interfaces, Component component)
        {
            if (interfaces.Contains(typeof(IMistJoined)))
            {
                var method = component.GetType().GetMethod("OnJoined",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null)
                {
                    var delegateInstance = Delegate.CreateDelegate(Expression.GetActionType(new[] { typeof(string) }), component, method);
                    MistManager.I.AddJoinedCallback((Action<string>)delegateInstance);
                }
            }    
            
            if (interfaces.Contains(typeof(IMistLeft)))
            {
                var method = component.GetType().GetMethod("OnLeft",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null)
                {
                    var delegateInstance = Delegate.CreateDelegate(Expression.GetActionType(new[] { typeof(string) }), component, method);
                    MistManager.I.AddLeftCallback((Action<string>)delegateInstance);
                }
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
                MistDebug.Log($"Found method: {methodInfo.Name} in component: {component.GetType().Name}");
                // 引数の種類に応じたDelegateを作成
                var argTypes = methodInfo.GetParameters().Select(p => p.ParameterType).ToArray();

                // 返り値がvoidかどうか
                var delegateType = methodInfo.ReturnType == typeof(void)
                    ? Expression.GetActionType(argTypes)
                    : Expression.GetFuncType(argTypes.Concat(new[] { methodInfo.ReturnType }).ToArray());

                var delegateInstance = Delegate.CreateDelegate(delegateType, component, methodInfo);
                var keyName = $"{Id}_{delegateInstance.Method.Name}";
                _rpcList.Add(keyName);

                var argTypesWithoutMessageInfo = argTypes.Where(t => t != typeof(MessageInfo)).ToArray();
                MistManager.I.AddRPC(keyName, delegateInstance, argTypesWithoutMessageInfo);
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
                // Action property.PropertyTypeの型を引数に取るActionを作成
                Action<object> wrapper = value =>
                {
                    originalSetMethod.Invoke(component, new[] { value });
                    onChangedMethodInfo?.Invoke(component, null);
                };

                // 登録
                var wrapperDelegate = Delegate.CreateDelegate(delegateType, wrapper.Target, wrapper.Method);

                _rpcList.Add(keyName);
                _propertyValueDict.Add(keyName, property.GetValue(component));
                MistManager.I.AddRPC(keyName, wrapperDelegate, new[] { property.PropertyType });
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

                    var propertyValue = _propertyValueDict[keyName];
                    if (propertyValue == null) continue;
                    if (propertyValue.Equals(value)) continue;

                    _propertyValueDict[keyName] = value;

                    MistDebug.Log($"Property: {property.Name}, Value: {value}");
                    MistManager.I.RPCAll(keyName, value);
                }

                await UniTask.Delay(TimeSpan.FromSeconds(syncIntervalSeconds), cancellationToken: token);
            }
        }
    }
}