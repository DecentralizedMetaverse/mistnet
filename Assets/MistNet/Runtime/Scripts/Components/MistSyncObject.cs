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
        [SerializeField] private bool _isOwner = true; // 表示用

        public bool IsOwner
        {
            // TODO: Test
            get => _isOwner;
            private set
            {
                _isOwner = value;

                if (IsOwner)
                {
                    // 後からOwnerになった際を考慮している
                    if (_tokenSource != null) return;
                    _tokenSource = new CancellationTokenSource();
                    WatchPropertiesAsync(_tokenSource.Token).Forget();
                }
                else
                {
                    _tokenSource?.Cancel();
                    _tokenSource = null;
                }
            }
        }

        public bool IsPlayerObject { get; set; }
        public bool IsGlobalObject { get; private set; } //合意Objectかどうか (全員が扱うことができるObjectかどうか)
        [HideInInspector] public MistTransform MistTransform;
        [SerializeField] private float syncIntervalSeconds = 0.5f;

        private readonly List<string> _rpcList = new();
        private readonly List<(Component, PropertyInfo)> _propertyList = new();
        private readonly Dictionary<string, object> _propertyValueDict = new();
        private CancellationTokenSource _tokenSource;
        private static int _instanceIdCount;

        private void Awake()
        {
            // Debug.Log($"[Debug] MistSyncObject Awake {gameObject.name}");
            gameObject.TryGetComponent(out MistTransform);
        }

        private void Start()
        {
            // Debug.Log($"[Debug] MistSyncObject Start {gameObject.name}");

            // 既にScene上に配置されたObjectである場合
            if (string.IsNullOrEmpty(Id))
            {
                SetGlobalObject();
            }

            RegisterPropertyAndRPC();

            if (IsGlobalObject) RequestOwner().Forget();
        }

        private void SetGlobalObject()
        {
            // Debug.Log($"[Debug] SetGlobalObject {gameObject.name}");
            // 自動合意Objectに設定する　どのNodeが変更しても、自動で合意をとって同期する
            var instanceId = _instanceIdCount++.ToString();
            Id = instanceId;
            IsOwner = false;
            IsGlobalObject = true;
            // OwnerId = MistPeerData.I.SelfId; // 自身のIDをOwnerとして設定しておく
            MistSyncManager.I.RegisterSyncObject(this);
        }

        private void OnDestroy()
        {
            _tokenSource?.Cancel();

            foreach (var rpc in _rpcList)
            {
                MistManager.I.RemoveRPC(rpc);
            }

            if (IsOwner) return;
            if (IsGlobalObject) return;
            MistSyncManager.I.UnregisterSyncObject(this);
        }

        public void SetData(string id, bool isOwner, string prefabAddress, string ownerId)
        {
            // Debug.Log($"[Debug] SetData {id}, {isOwner}, {prefabAddress}, {ownerId}");
            Id = id;
            IsOwner = isOwner;
            PrefabAddress = prefabAddress;
            OwnerId = ownerId;
            gameObject.TryGetComponent(out MistTransform);

            if (IsOwner) MistSyncManager.I.SelfSyncObject = this;
        }

        private int _ownerRequestCount;
        private bool _receiveAnswer = false;

        /// <summary>
        /// 既にSceneにあるObject用である
        /// TODO: Ownerが切断された場合、次に誰がOwnerになるかを決定する必要がある
        /// </summary>
        public async UniTask RequestOwner()
        {
            if (IsOwner) return;
            _receiveAnswer = false;
            _ownerRequestCount++;
            do
            {
                RPCOther(nameof(RequestOwner), MistPeerData.I.SelfId, _ownerRequestCount);
                await UniTask.Delay(TimeSpan.FromSeconds(1));
            } while (!_receiveAnswer);
        }

        // TODO: 要検証
        // TODO: 後から入出する人はRPCが実行されない
        [MistRpc]
        private void RequestOwner(string id, int ownerRequestCount)
        {
            // Debug.Log($"[Debug][0] RequestOwner {id}, {ownerRequestCount}");
            const int threshold = 100;
            if (ownerRequestCount == int.MinValue && _ownerRequestCount > int.MaxValue - threshold)
            {
                // オーバーフローを考慮
            }
            else if (ownerRequestCount < _ownerRequestCount)
            {
                // 新しいリクエストカウントが現在のカウントより小さい場合は無視 つまり誰かがOwnerになっているということ
                RPC(id, nameof(OnReceiveOwnerRequestCount), _ownerRequestCount, OwnerId);
                return;
            }

            OwnerId = id;
            IsOwner = false;
            _ownerRequestCount = ownerRequestCount;
            RPC(id, nameof(OnChangedOwner));
        }

        [MistRpc]
        public void OnReceiveOwnerRequestCount(int count, string ownerId)
        {
            // Debug.Log($"[Debug][0] OnReceiveOwnerRequestCount {count}");
            _ownerRequestCount = count;
            OwnerId = ownerId;
            IsOwner = false;
            _receiveAnswer = true;
        }

        [MistRpc]
        private void OnChangedOwner()
        {
            // Debug.Log($"[Debug][0] OnChangedOwner {OwnerId}");
            OwnerId = Id;
            IsOwner = true;
            _receiveAnswer = true;
        }

        // -------------------

        public void RPC(string targetId, string key, params object[] args)
        {
            MistManager.I.RPC(targetId, GetRPCName(key), args);
        }

        public void RPCOther(string key, params object[] args)
        {
            MistManager.I.RPCOther(GetRPCName(key), args);
        }

        public void RPCAll(string key, params object[] args)
        {
            MistManager.I.RPCAll(GetRPCName(key), args);
        }

        private string GetRPCName(string methodName)
        {
            return $"{Id}_{methodName}";
        }

        // -------------------

        public void SendAllProperties(string id)
        {
            foreach (var (component, property) in _propertyList)
            {
                var value = property.GetValue(component);
                MistManager.I.RPC(id, GetRPCName(property.Name), value);
            }
        }

        private void RegisterPropertyAndRPC()
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
                    var delegateInstance = Delegate.CreateDelegate(Expression.GetActionType(new[] { typeof(string) }),
                        component, method);
                    MistManager.I.AddJoinedCallback((Action<string>)delegateInstance);
                }
            }

            if (interfaces.Contains(typeof(IMistLeft)))
            {
                var method = component.GetType().GetMethod("OnLeft",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null)
                {
                    var delegateInstance = Delegate.CreateDelegate(Expression.GetActionType(new[] { typeof(string) }),
                        component, method);
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
                var keyName = GetRPCName(delegateInstance.Method.Name);
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
                    MistManager.I.RPCOther(keyName, value);
                }

                await UniTask.Delay(TimeSpan.FromSeconds(syncIntervalSeconds), cancellationToken: token);
            }
        }
    }
}