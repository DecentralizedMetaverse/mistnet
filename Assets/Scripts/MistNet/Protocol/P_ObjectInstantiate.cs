using MemoryPack;
using UnityEngine;

[MemoryPackable]
public partial class P_ObjectInstantiate
{
    public string ObjId { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public string PrefabAddress { get; set; }
}
