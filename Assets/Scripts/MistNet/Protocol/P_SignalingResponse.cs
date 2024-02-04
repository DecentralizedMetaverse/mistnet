using MemoryPack;
using UnityEngine;

[MemoryPackable]
public partial class P_SignalingResponse
{
    public string TargetId { get; set; }
    public string Request { get; set; }
}
