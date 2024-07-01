using UnityEngine;

namespace MistNet.Runtime.Scripts.Utils
{
    public static class MistUtils
    {
        public static (int, int, int) GetChunk(Vector3 position)
        {
            return ((int)position.x / MistConstraints.ChunkSize, (int)position.y / MistConstraints.ChunkSize, (int)position.z / MistConstraints.ChunkSize);
        }
    }
}
