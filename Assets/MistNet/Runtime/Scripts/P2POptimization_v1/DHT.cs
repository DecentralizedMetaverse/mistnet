using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Numerics;

/// <summary>
/// Kademlia Distributed Hash Table
/// </summary>
public class DHT
{
    public const int IDLength = 20;
    private byte[] id;

    public DHT(byte[] id)
    {
        this.id = id;
    }

    public DHT(string data)
    {
        using SHA1 sha1 = SHA1.Create();
        id = sha1.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    public override string ToString()
    {
        return BitConverter.ToString(id).Replace("-", "").ToLower();
    }

    public BigInteger Xor(DHT other)
    {
        var distance = new BigInteger(id);
        var otherInt = new BigInteger(other.id);
        return distance ^ otherInt;
    }

    public int BitLength()
    {
        return this.id.Length * 8;
    }

    public byte[] ToByteArray()
    {
        return this.id;
    }
}

public class Contact
{
    public DHT ID { get; set; }
    public string Address { get; set; }

    public Contact(DHT id, string address)
    {
        ID = id;
        Address = address;
    }
}

public class RoutingTable
{
    public Contact Self { get; private set; }
    public List<Contact>[] Buckets { get; private set; }
    private readonly object _lockObj = new object();

    public RoutingTable(Contact self)
    {
        Self = self;
        Buckets = new List<Contact>[DHT.IDLength * 8];
        for (var i = 0; i < Buckets.Length; i++)
        {
            Buckets[i] = new List<Contact>();
        }
    }

    public void AddContact(Contact contact)
    {
        lock (_lockObj)
        {
            var bucketIndex = BucketIndex(contact.ID);
            Buckets[bucketIndex].Add(contact);
        }
    }

    public int BucketIndex(DHT id)
    {
        var distance = Self.ID.Xor(id);
        return distance.GetBitLength() - 1;
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
                : (null, false);
        }
    }

    public List<Contact> FindNode(DHT id)
    {
        var bucketIndex = _routingTable.BucketIndex(id);
        Console.WriteLine("Bucket index: " + bucketIndex);
        return _routingTable.Buckets[bucketIndex];
    }
}

public static class BigIntegerExtensions
{
    public static int GetBitLength(this BigInteger value)
    {
        var bitLength = 0;
        while (value > 0)
        {
            bitLength++;
            value >>= 1;
        }

        return bitLength;
    }
}