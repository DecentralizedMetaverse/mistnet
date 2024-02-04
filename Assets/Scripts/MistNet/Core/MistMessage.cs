using MemoryPack;

[MemoryPackable]
public partial class MistMessage
{
    public MistNetMessageType Type;
    public string Chunk;
    public string TargetId;
    public string Id;
    public byte[] Data;
}
