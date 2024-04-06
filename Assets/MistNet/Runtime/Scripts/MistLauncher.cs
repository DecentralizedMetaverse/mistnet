using Cysharp.Threading.Tasks;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MistNet
{
    public class MistLauncher : MonoBehaviour
    {
        private static readonly int MaxRange = 1000;
        [SerializeField] private string prefabAddress = "Assets/Prefab/MistNet/MistPlayerTest.prefab";
        [SerializeField] private bool randomSpawn;
        [SerializeField] private bool yFixed;
        
        private void Start()
        {
            // 座標をランダムで取得する
            var position = Vector3.zero;
            if (randomSpawn)
            {
                var x = Random.Range(-MaxRange, MaxRange);
                var y = yFixed ? 0 : Random.Range(-MaxRange, MaxRange);
                var z = Random.Range(-MaxRange, MaxRange);
                position = new Vector3(x, y, z);
            }
            MistManager.I.InstantiateAsync(prefabAddress, position, Quaternion.identity).Forget();
        }
    }
}