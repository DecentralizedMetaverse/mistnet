using MemoryPack;
using UnityEngine;

[MemoryPackable]
public partial class P_ConnectedList
{
    public string[] Ids { get; set; }
    public string[] Chunk { get; set; }
}
