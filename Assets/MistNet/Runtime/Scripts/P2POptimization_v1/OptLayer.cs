using System.Text;
using UnityEngine;

public class OptLayer : MonoBehaviour
{
    [ContextMenu("RunOptLayer")]
    async void Start()
    {
        // ノードの作成
        var selfNodeId = new NodeId("self-node");
        var selfNode = new Node(selfNodeId, "127.0.0.1");

        var kademlia = new Kademlia(selfNode);

        // 他のノードを追加
        var otherNodeId1 = new NodeId("other-node-1");
        var otherNode1 = new Node(otherNodeId1, "127.0.0.2");
        kademlia.RoutingTable.AddNode(otherNode1);

        var otherNodeId2 = new NodeId("other-node-2");
        var otherNode2 = new Node(otherNodeId2, "127.0.0.3");
        kademlia.RoutingTable.AddNode(otherNode2);

        // データの保存
        string key = "sample-key";
        string value = "Hello, Kademlia!";
        bool storeResult = await kademlia.StoreAsync(key, value);
        Debug.Log($"Store result: {storeResult}");

        // データの検索
        var (foundValue, found) = await kademlia.FindValueAsync(key);
        if (found)
        {
            Debug.Log($"Found value: {Encoding.UTF8.GetString(foundValue)}");
        }
        else
        {
            Debug.Log("Value not found");
        }

        // ノードの検索
        var targetNodeId = new NodeId("other-node-2");
        var closestNodes = await kademlia.FindNodeAsync(targetNodeId);
        Debug.Log("Closest nodes:");
        foreach (var node in closestNodes)
        {
            Debug.Log($"Node ID: {node.Id}, Address: {node.Address}");
        }

    }
}
