using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MistNet
{
    public class MistLauncher : MonoBehaviour
    {
        private static readonly int MaxRange = 1000;
        public string PrefabAddress = "Assets/Prefab/MistNet/MistPlayerTest.prefab";
        
        [SerializeField] private bool randomSpawn = false;
        
        private void Start()
        {
            // 座標をランダムで取得する
            var position = Vector3.zero;
            if (randomSpawn)
            {
                var x = Random.Range(-MaxRange, MaxRange);
                var y = Random.Range(-MaxRange, MaxRange);
                var z = Random.Range(-MaxRange, MaxRange);
                position = new Vector3(x, y, z);
            }
            MistManager.I.InstantiateAsync(PrefabAddress, position, Quaternion.identity).Forget();
            // MistManager.I.AddRpc<string, int>(RPCTest);
        }

        [MistSync]
        public string testVariable = "test";
        
        [MistRpc]
        private int RPCTest(string a)
        {
            return 0;
        }

        private void Test()
        {
            // MistManager.I.Rpc(nameof(RPCTest), MistTarget.All);
        }
    }
}