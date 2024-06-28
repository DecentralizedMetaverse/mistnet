using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Numerics;
using System.Linq;
using Cysharp.Threading.Tasks;
using MistNet;
using UnityEngine;

public class NodeId
{
    public const int Length = 20; // 160ビット
    private readonly byte[] _id;

    public NodeId(byte[] id)
    {
        if (id.Length != Length)
            throw new ArgumentException($"ID must be {Length} bytes long", nameof(id));
        _id = id;
    }

    public NodeId(string data)
    {
        using var sha1 = SHA1.Create();
        _id = sha1.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    public BigInteger Distance(NodeId other) => new BigInteger(_id.Zip(other._id, (a, b) => (byte)(a ^ b)).ToArray());

    public override string ToString() => BitConverter.ToString(_id).Replace("-", "").ToLower();

    public byte[] ToByteArray() => _id;

    public override bool Equals(object obj)
    {
        if (obj is NodeId other)
        {
            return _id.SequenceEqual(other._id);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return BitConverter.ToInt32(_id, 0);
    }
}

public class Node
{
    public NodeId Id { get; }
    public string Address { get; }

    public Node(NodeId id, string address)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Address = address ?? throw new ArgumentNullException(nameof(address));
    }
}

public class RoutingTable
{
    public const int K = 20; // Kademliaパラメータ: バケットあたりの最大ノード数
    private readonly Node _self;
    private readonly List<Node>[] _buckets;
    public Func<Node, UniTask<bool>> PingAsync;

    public RoutingTable(Node self)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
        _buckets = Enumerable.Range(0, NodeId.Length * 8).Select(_ => new List<Node>()).ToArray();
    }

    public async UniTask AddNodeAsync(Node node)
    {
        Debug.Log($"[Debug] AddNodeAsync selfId: {_self.Id}\nnode: {node.Id}");
        if (node.Id.Equals(_self.Id)) return;

        var bucketIndex = GetBucketIndex(_self.Id.Distance(node.Id));
        var bucket = _buckets[bucketIndex];

        lock (bucket)
        {
            var existingNode = bucket.FirstOrDefault(n => n.Id.Equals(node.Id));
            if (existingNode != null)
            {
                // 同じノードが見つかった場合、先頭に移動
                bucket.Remove(existingNode);
                bucket.Insert(0, node);
                return;
            }

            if (bucket.Count < K)
            {
                bucket.Insert(0, node);
                return;
            }
        }

        // バケットが満杯の場合、最後のノードをpingして応答がなければ置き換える
        var lastNode = bucket[^1];
        var isAlive = await PingAsync(lastNode);

        lock (bucket)
        {
            if (isAlive) return; // 最後のノードが生きている場合、新しいノードは追加しない

            bucket.RemoveAt(bucket.Count - 1);
            bucket.Insert(0, node);
        }
    }

    public List<Node> GetClosestNodes(NodeId targetId, int count)
    {
        var allNodes = _buckets.SelectMany(b => b).ToList();
        return allNodes
            .OrderBy(n => n.Id.Distance(targetId))
            .Take(count)
            .ToList();
    }

    private static int GetBucketIndex(BigInteger distance)
    {
        return distance == 0 ? 0 : NodeId.Length * 8 - 1 - distance.GetBitLength();
    }
}

public class Kademlia
{
    private readonly Node _self;
    public readonly RoutingTable RoutingTable;
    private readonly Dictionary<NodeId, byte[]> _dataStore = new();
    private readonly Dictionary<string, UniTaskCompletionSource<bool>> _pendingPingRequests = new();
    private readonly Dictionary<string, UniTaskCompletionSource<List<Node>>> _pendingFindNodeRequests = new();

    private readonly Dictionary<string, UniTaskCompletionSource<(byte[] value, bool found)>> _pendingFindValueRequests =
        new();

    public Action<string, string, string> SendMessage { get; set; }

    private readonly Dictionary<string, Node> _nodes = new();
    private readonly Dictionary<string, NodeId> _nodeIds = new();

    public Kademlia(Node self)
    {
        _self = self;
        RoutingTable = new RoutingTable(self)
        {
            PingAsync = PingAsync
        };
    }

    public async UniTask<bool> PingAsync(Node node)
    {
        SendMessage(node.Address, "PING", "");
        _pendingPingRequests[node.Address] = new UniTaskCompletionSource<bool>();
        return await _pendingPingRequests[node.Address].Task;
    }

    public void StoreAsync(string key, string value)
    {
        var keyId = GetNodeId(key);
        var nodes = RoutingTable.GetClosestNodes(keyId, RoutingTable.K);

        foreach (var node in nodes)
        {
            SendMessage(node.Address, "STORE", $"{key}|{value}");
        }
    }

    public async UniTask<List<Node>> FindNodeAsync(NodeId targetId)
    {
        var closestNodes = RoutingTable.GetClosestNodes(targetId, RoutingTable.K);
        var queried = new HashSet<NodeId>();
        var toQuery = new Queue<Node>(closestNodes);
        const int maxIterations = 10; // 最大の問い合わせ回数を設定
        int iterations = 0;

        Debug.Log($"[Debug] FindNodeAsync selfId: {MistPeerData.I.SelfId}\ntarget: {targetId}");
        foreach (var node in closestNodes)
        {
            Debug.Log($"[Debug] FindNodeAsync queried: {node.Id}");
        }

        while (toQuery.Count > 0 && iterations < maxIterations)
        {
            var node = toQuery.Dequeue();
            if (queried.Contains(node.Id)) continue;

            SendMessage(node.Address, "FIND_NODE", targetId.ToString());

            _pendingFindNodeRequests[node.Address] = new UniTaskCompletionSource<List<Node>>();

            List<Node> nodes;
            try
            {
                // タイムアウトの設定
                nodes = await _pendingFindNodeRequests[node.Address].Task.Timeout(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                // タイムアウトした場合は次のノードへ
                continue;
            }

            lock (queried)
            {
                foreach (var newNode in nodes)
                {
                    if (!queried.Contains(newNode.Id))
                    {
                        toQuery.Enqueue(newNode);
                    }
                }

                queried.Add(node.Id);
            }

            closestNodes = closestNodes
                .Concat(nodes)
                .OrderBy(n => n.Id.Distance(targetId))
                .Take(RoutingTable.K)
                .ToList();

            iterations++;
        }

        return closestNodes;
    }

    public async UniTask<(byte[], bool)> FindValueAsync(string key)
    {
        var keyId = GetNodeId(key);
        Debug.Log($"[Debug] FindValueAsync key: {key} -> {keyId}");

        if (_dataStore.TryGetValue(keyId, out var value))
        {
            return (value, true);
        }

        // 見つからない場合、最も近いノードに問い合わせる
        Debug.Log($"[Debug] FindValueAsync Cannot find value for key: {key}");
        var nodes = await FindNodeAsync(keyId);
        Debug.Log($"[Debug] FindValueAsync debug: {nodes.Count} nodes found for key: {key}");
        foreach (var node in nodes)
        {
            Debug.Log($"[Debug] FindValueAsync Querying node: {node.Address}");
            SendMessage(node.Address, "FIND_VALUE", key);
            // 応答があるまで待つ
            _pendingFindValueRequests[node.Address] = new UniTaskCompletionSource<(byte[], bool)>();
            var (data, found) = await _pendingFindValueRequests[node.Address].Task;

            if (found)
            {
                return (data, true);
            }
        }

        return (null, false);
    }

    public void ReceiveMessageAsync(string address, string type, string data)
    {
        var sender = new Node(new NodeId(address), address); // addressからNodeIdを生成してNodeオブジェクトを作成
        switch (type)
        {
            case "PING":
                HandlePing(sender, data);
                break;
            case "STORE":
                HandleStore(sender, data);
                break;
            case "FIND_NODE":
                HandleFindNode(sender, data);
                break;
            case "FIND_VALUE":
                HandleReceiveFindValue(sender, data);
                break;
            case "PONG":
                Pong(sender, data);
                break;
            case "FIND_NODE_RESPONSE":
                FindNodeResponse(sender, data);
                break;
            case "FIND_VALUE_RESPONSE":
                FindValueResponse(sender, data);
                break;
            default:
                throw new InvalidOperationException($"Unknown message type: {type}");
        }
    }

    private void HandlePing(Node sender, string data)
    {
        SendMessage(sender.Address, "PONG", "");
    }

    private void HandleStore(Node sender, string data)
    {
        var parts = data.Split('|');
        var key = parts[0];
        var value = Encoding.UTF8.GetBytes(parts[1]);
        var keyId = GetNodeId(key);
        _dataStore[keyId] = value;
    }

    private void HandleFindNode(Node sender, string data)
    {
        var keyId = GetNodeId(data);
        var nodes = RoutingTable.GetClosestNodes(keyId, RoutingTable.K);
        var nodeIds = nodes.Select(n => n.Id.ToString()).ToArray();
        SendMessage(sender.Address, "FIND_NODE_RESPONSE", string.Join(",", nodeIds));
    }

    private void HandleReceiveFindValue(Node sender, string data)
    {
        RoutingTable.AddNodeAsync(sender).Forget();
        var keyId = GetNodeId(data);
        if (_dataStore.TryGetValue(keyId, out var value))
        {
            SendMessage(sender.Address, "FIND_VALUE_RESPONSE", $"{keyId}|{value}");
        }
        else
        {
            var nodes = RoutingTable.GetClosestNodes(keyId, RoutingTable.K);
            var nodeIds = nodes.Select(n => n.Id.ToString()).ToArray();
            SendMessage(sender.Address, "FIND_NODE_RESPONSE", string.Join(",", nodeIds));
        }
    }

    private void FindNodeResponse(Node sender, string data)
    {
        var nodeIds = data.Split(',');
        var nodes = nodeIds.Select(GetNode).ToList();
        _pendingFindNodeRequests[sender.Address].TrySetResult(nodes);
    }

    private void FindValueResponse(Node sender, string data)
    {
        var parts = data.Split('|');
        var value = Encoding.UTF8.GetBytes(parts[1]);
        _pendingFindValueRequests[sender.Address].TrySetResult((value, true));
    }

    private void Pong(Node sender, string data)
    {
        _pendingPingRequests[sender.Address].TrySetResult(true);
    }

    #region Utility

    private Node GetNode(string id)
    {
        if (!_nodes.TryGetValue(id, out var node))
        {
            _nodes.Add(id, new Node(new NodeId(id), id));
        }

        return _nodes[id];
    }

    private NodeId GetNodeId(string id)
    {
        if (!_nodeIds.TryGetValue(id, out var nodeId))
        {
            _nodeIds.Add(id, new NodeId(id));
        }

        return _nodeIds[id];
    }

    #endregion
}

public static class BigIntegerExtensions
{
    public static int GetBitLength(this BigInteger value)
    {
        return value == 0 ? 1 : (int)BigInteger.Log(BigInteger.Abs(value), 2) + 1;
    }
}

public static class QueueExtensions
{
    public static IEnumerable<T> DequeueRange<T>(this Queue<T> queue, int count)
    {
        count = Math.Min(count, queue.Count);
        for (int i = 0; i < count; i++)
        {
            yield return queue.Dequeue();
        }
    }
}