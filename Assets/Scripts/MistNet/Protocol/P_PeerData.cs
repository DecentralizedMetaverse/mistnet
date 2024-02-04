using MemoryPack;
using UnityEngine;

[MemoryPackable]
public partial class P_PeerData
{
    public string Id { get; set; }
    public string Chunk { get; set; }
    public Vector3 Position { get; set; }
    public int CurrentConnectNum { get; set; }
    public int MinConnectNum { get; set; }
    public int LimitConnectNum { get; set; }
    public int MaxConnectNum { get; set; }
}
