using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MistNet
{
    public class MistRpcManager : MonoBehaviour
    {
        private readonly Dictionary<string, Delegate> _rpcFunctionDict = new();
        private void Awake()
        {
            GetAllRpc();
        }

        private void RegisterRpc(Delegate function)
        {
            _rpcFunctionDict.Add(function.Method.Name, function);
        }

        // RPC登録用関数
        #region AddRPC
        public void AddRpc(Action function)
        {
            RegisterRpc(function);
        }
        
        public void AddRpc<T>(Action<T> function)
        {
            RegisterRpc(function);
        }
        
        public void AddRpc<T1, T2>(Action<T1, T2> function)
        {
            RegisterRpc(function);
        }
        
        public void AddRpc<T1, T2, T3>(Action<T1, T2, T3> function)
        {
            RegisterRpc(function);
        }
        
        public void AddRpc<T1, T2, T3, T4>(Action<T1, T2, T3, T4> function)
        {
            RegisterRpc(function);
        }
        
        public void AddRpc<T>(Func<T> function)
        {
            RegisterRpc(function);
        }
        
        public void AddRpc<T1, T2>(Func<T1, T2> function)
        {
            RegisterRpc(function);
        }
        
        public void AddRpc<T1, T2, T3>(Func<T1, T2, T3> function)
        {
            RegisterRpc(function);
        }
        
        public void AddRpc<T1, T2, T3, T4>(Func<T1, T2, T3, T4> function)
        {
            RegisterRpc(function);
        }
        #endregion
        
        private void GetAllRpc()
        {
            var methods = typeof(MistRpcAttribute).GetMethods();
            foreach (var method in methods)
            {
                var attribute = method.GetCustomAttribute<MistRpcAttribute>();
                if (attribute != null)
                {
                    Debug.Log($"RPC: {method.Name}");
                }
            }
        }
        
        public void Rpc(string rpcName, MistTarget target, params object[] args)
        {
            
        }
        
        public void Rpc(string rpcName, string targetId, params object[] args)
        {
            
        }
    }
}
