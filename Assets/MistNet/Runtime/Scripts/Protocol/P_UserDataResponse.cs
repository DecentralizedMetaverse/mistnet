using MemoryPack;
using UnityEngine;

[MemoryPackable]
public partial class P_UserDataResponse
{
    public (int, int, int) Chunk { get; set; }
}
