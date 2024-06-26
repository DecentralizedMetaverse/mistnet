using UnityEngine;

public class OptLayer : MonoBehaviour
{
    [ContextMenu("RunOptLayer")]
    void Start()
    {
        var nodeId = new DHT("node1");
        var node = new Contact(nodeId, "127.0.0.1:5000");

        var kademlia = new Kademlia(node);
        kademlia.Store("(1,1,1)", "nodeA");
        kademlia.Store("(2,2,2)", "nodeB");

        var (val, found) = kademlia.FindValue("(1,1,1)");
        Debug.Log($"val: {val}, found: {found}");

        (val, found) = kademlia.FindValue("nodeA");
        Debug.Log($"val: {val}, found: {found}");

    }
}
