using MemoryPack;
using UnityEngine;

[MemoryPackable]
public partial class P_RPC
{
    public string Method { get; set; }
    public string Args { get; set; }
}
