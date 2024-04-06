using MemoryPack;
using UnityEngine;

[MemoryPackable]
public partial class P_Location
{
    public string ObjId { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public Vector3 Velocity { get; set; }
    public float Time { get; set; }
}
