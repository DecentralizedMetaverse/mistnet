using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Numerics;
using System.Linq;
using Cysharp.Threading.Tasks;

public class NodeId
{
    public const int LENGTH = 20; // 160ビット
    private readonly byte[] _id;

    public NodeId(byte[] id)
    {
        if (id.Length != LENGTH)
            throw new ArgumentException($"ID must be {LENGTH} bytes long", nameof(id));
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

    public RoutingTable(Node self)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
        _buckets = Enumerable.Range(0, NodeId.LENGTH * 8).Select(_ => new List<Node>()).ToArray();
    }

    public void AddNode(Node node)
    {
        if (node.Id.Equals(_self.Id)) return;

        var bucketIndex = GetBucketIndex(_self.Id.Distance(node.Id));
        var bucket = _buckets[bucketIndex];

        lock (bucket)
        {
            var existingNode = bucket.FirstOrDefault(n => n.Id.Equals(node.Id));
            if (existingNode != null)
            {
                bucket.Remove(existingNode);
                bucket.Insert(0, node);
            }
            else if (bucket.Count < K)
            {
                bucket.Insert(0, node);
            }
            else
            {
                // バケットが満杯の場合、最後のノードをpingして応答がなければ置き換える
                // 実際の実装では、非同期でpingを行い、応答に基づいて処理する
                bucket.RemoveAt(bucket.Count - 1);
                bucket.Insert(0, node);
            }
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
        return distance == 0 ? 0 : NodeId.LENGTH * 8 - distance.GetBitLength();
    }
}

public class Kademlia
{
    private const bool DebugLatency = false;
    private readonly Node _self;
    public readonly RoutingTable RoutingTable;
    private readonly Dictionary<NodeId, byte[]> _dataStore = new();

    // 送信用アクションデリゲート
    public Action<Node> SendPing { get; set; }
    public Action<Node, string, string> SendStore { get; set; }
    public Action<Node, NodeId> SendFindNode { get; set; }
    public Action<Node, string> SendFindValue { get; set; }

    public Kademlia(Node self)
    {
        _self = self;
        RoutingTable = new RoutingTable(self);
    }

    // 受信メソッド（public メソッド）
    public bool ReceivePing(Node sender)
    {
        RoutingTable.AddNode(sender);
        return true;
    }

    public void ReceiveStore(Node sender, string key, string value)
    {
        var keyId = new NodeId(key);
        _dataStore[keyId] = Encoding.UTF8.GetBytes(value);
        RoutingTable.AddNode(sender);
    }

    public List<Node> ReceiveFindNode(Node sender, NodeId targetId)
    {
        RoutingTable.AddNode(sender);
        return RoutingTable.GetClosestNodes(targetId, RoutingTable.K);
    }

    public (byte[] value, bool found) ReceiveFindValue(Node sender, string key)
    {
        RoutingTable.AddNode(sender);
        var keyId = new NodeId(key);
        if (_dataStore.TryGetValue(keyId, out var value))
        {
            return (value, true);
        }
        return (Array.Empty<byte>(), false);
    }

    // 内部処理メソッド
    public async UniTask<bool> PingAsync(Node node)
    {
        if (DebugLatency) await SimulateNetworkDelay();
        SendPing?.Invoke(node);
        // 実際のネットワーク実装では、ここでノードからの応答を待つ必要があります
        return true; // 仮の戻り値
    }

    public async UniTask<bool> StoreAsync(string key, string value)
    {
        var keyId = new NodeId(key);
        var nodes = RoutingTable.GetClosestNodes(keyId, RoutingTable.K);

        foreach (var node in nodes)
        {
            if (DebugLatency) await SimulateNetworkDelay();
            SendStore?.Invoke(node, key, value);
        }

        return true;
    }

    public async UniTask<List<Node>> FindNodeAsync(NodeId targetId)
    {
        var closestNodes = RoutingTable.GetClosestNodes(targetId, RoutingTable.K);
        var queried = new HashSet<NodeId>();
        var toQuery = new Queue<Node>(closestNodes);

        while (toQuery.Count > 0)
        {
            var node = toQuery.Dequeue();
            if (queried.Contains(node.Id)) continue;

            if (DebugLatency) await SimulateNetworkDelay();
            SendFindNode?.Invoke(node, targetId);

            // 実際のネットワーク実装では、ここでノードからの応答を待つ必要があります
            var newNodes = RoutingTable.GetClosestNodes(targetId, RoutingTable.K);

            foreach (var newNode in newNodes)
            {
                if (!queried.Contains(newNode.Id))
                {
                    toQuery.Enqueue(newNode);
                }
            }

            queried.Add(node.Id);
            closestNodes = closestNodes
                .Concat(newNodes)
                .OrderBy(n => n.Id.Distance(targetId))
                .Take(RoutingTable.K)
                .ToList();
        }

        return closestNodes;
    }

    public async UniTask<(byte[] value, bool found)> FindValueAsync(string key)
    {
        var keyId = new NodeId(key);

        if (_dataStore.TryGetValue(keyId, out var localValue))
        {
            return (localValue, true);
        }

        var nodes = await FindNodeAsync(keyId);
        foreach (var node in nodes)
        {
            if (DebugLatency) await SimulateNetworkDelay();
            SendFindValue?.Invoke(node, key);
            // 実際のネットワーク実装では、ここで各ノードからの応答を待つ必要があります
        }

        return (Array.Empty<byte>(), false);
    }

    private async UniTask SimulateNetworkDelay()
    {
        await UniTask.Delay(new Random().Next(10, 100)); // 10-100ms のランダムな遅延
    }
}

public static class BigIntegerExtensions
{
    public static int GetBitLength(this BigInteger value)
    {
        return value == 0 ? 1 : (int)BigInteger.Log(BigInteger.Abs(value), 2) + 1;
    }
}
