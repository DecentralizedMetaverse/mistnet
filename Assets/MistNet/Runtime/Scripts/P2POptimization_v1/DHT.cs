using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Numerics;
using System.Linq;

public class DHT
{
    public const int IDLength = 20;
    private readonly byte[] id;

    public DHT(byte[] id)
    {
        if (id.Length != IDLength)
            throw new ArgumentException($"ID must be {IDLength} bytes long", nameof(id));
        this.id = id;
    }

    public DHT(string data)
    {
        using var sha1 = SHA1.Create();
        id = sha1.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    public override string ToString()
    {
        return BitConverter.ToString(id).Replace("-", "").ToLower();
    }

    public BigInteger Xor(DHT other)
    {
        return new BigInteger(id.Zip(other.id, (a, b) => (byte)(a ^ b)).ToArray());
    }

    public int BitLength() => id.Length * 8;

    public byte[] ToByteArray() => id;
}

public class Contact
{
    public DHT ID { get; }
    public string Address { get; }

    public Contact(DHT id, string address)
    {
        ID = id ?? throw new ArgumentNullException(nameof(id));
        Address = address ?? throw new ArgumentNullException(nameof(address));
    }
}

public class RoutingTable
{
    public Contact Self { get; }
    public List<Contact>[] Buckets { get; }
    public const int K = 20; // Kademlia parameter: max number of contacts per bucket
    private readonly object _lockObj = new object();

    public RoutingTable(Contact self)
    {
        Self = self ?? throw new ArgumentNullException(nameof(self));
        Buckets = new List<Contact>[DHT.IDLength * 8];
        for (var i = 0; i < Buckets.Length; i++)
        {
            Buckets[i] = new List<Contact>();
        }
    }

    public void AddContact(Contact contact)
    {
        if (contact.ID.Equals(Self.ID)) return; // Don't add self

        lock (_lockObj)
        {
            var bucketIndex = BucketIndex(contact.ID);
            var bucket = Buckets[bucketIndex];

            var existingContact = bucket.FirstOrDefault(c => c.ID.Equals(contact.ID));
            if (existingContact != null)
            {
                bucket.Remove(existingContact);
                bucket.Insert(0, contact); // Move to front (most recently seen)
            }
            else if (bucket.Count < K)
            {
                bucket.Insert(0, contact);
            }
            else
            {
                // Bucket is full, consider replacing least-recently seen
                // In a real implementation, you'd ping the least-recently seen contact
                // and only replace it if it doesn't respond
                bucket.RemoveAt(bucket.Count - 1);
                bucket.Insert(0, contact);
            }
        }
    }

    public int BucketIndex(DHT id)
    {
        var distance = Self.ID.Xor(id);
        return DHT.IDLength * 8 - 1 - distance.GetBitLength();
    }
}

public class Kademlia
{
    private readonly RoutingTable _routingTable;
    private readonly Dictionary<DHT, byte[]> _dataStore = new();
    private readonly object _lockObj = new();

    public Kademlia(Contact self)
    {
        _routingTable = new RoutingTable(self);
    }

    public void Store(string key, string value)
    {
        lock (_lockObj)
        {
            var id = new DHT(key);
            _dataStore[id] = Encoding.UTF8.GetBytes(value);
        }
    }

    public (byte[] value, bool found) FindValue(string key)
    {
        lock (_lockObj)
        {
            var id = new DHT(key);
            return _dataStore.TryGetValue(id, out var value)
                ? (value, true)
                : (Array.Empty<byte>(), false);
        }
    }

    public List<Contact> FindNode(DHT target)
    {
        var closestNodes = new SortedList<BigInteger, Contact>();

        foreach (var bucket in _routingTable.Buckets)
        {
            foreach (var contact in bucket)
            {
                var distance = contact.ID.Xor(target);
                closestNodes.Add(distance, contact);

                if (closestNodes.Count > RoutingTable.K)
                    closestNodes.RemoveAt(closestNodes.Count - 1);
            }
        }

        return closestNodes.Values.ToList();
    }
}

public static class BigIntegerExtensions
{
    public static int GetBitLength(this BigInteger value)
    {
        return value == 0 ? 1 : (int)BigInteger.Log(BigInteger.Abs(value), 2) + 1;
    }
}