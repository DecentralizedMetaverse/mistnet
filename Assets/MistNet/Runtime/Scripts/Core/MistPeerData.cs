using System;
using System.Collections.Generic;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;

namespace MistNet
{
    public class MistPeerData
    {
        public static MistPeerData I { get; private set; } = new();
        public string SelfId { get; private set; }
        public Dictionary<string, MistPeerDataElement> GetAllPeer => _dict;
        public List<MistPeerDataElement> GetConnectedPeer => 
            _dict.Values.Where(x => x.State is MistPeerState.Connected or MistPeerState.Disconnecting).ToList();
        
        private readonly Dictionary<string, MistPeerDataElement> _dict = new();

        public void Init()
        {
            I = this;
            SelfId = Guid.NewGuid().ToString("N");
            MistDebug.Log($"[Self ID] {SelfId}");
            _dict.Clear();
        }
        
        public void AllForceClose()
        {
            foreach (var peerData in _dict.Values)
            {
                peerData.Peer.ForceClose();
            }
        }

        public bool IsConnected(string id)
        {
            if (!_dict.TryGetValue(id, out var data)) return false;
            return data.IsConnected;
        }

        public MistPeer GetPeer(string id)
        {
            if (_dict.TryGetValue(id, out var peerData))
            {
                peerData.Peer.Id = id;
                return peerData.Peer;
            }
            
            _dict.Add(id, new MistPeerDataElement(id));
            return _dict[id].Peer;
        }

        public MistPeerDataElement GetPeerData(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                MistDebug.LogError("GetPeerData id is null");
            }
            MistDebug.Log($"[GetPeerData] {id}");
            return _dict.TryGetValue(id, out var peerData) ? 
                peerData : null;
        }
        
        public void SetState(string id, MistPeerState state)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!_dict.TryGetValue(id, out var peerData)) return;
            peerData.State = state;
        }

        public void UpdatePeerData(string id, P_PeerData data)
        {
            if (string.IsNullOrEmpty(id)) return;
            
            _dict.TryAdd(id, new MistPeerDataElement(id));

            var peerData = _dict[id];
            peerData.Id = id;
            peerData.Peer.Id = id;
            peerData.Position = data.Position;
            peerData.CurrentConnectNum = data.CurrentConnectNum;
            peerData.MinConnectNum = data.MinConnectNum;
            peerData.LimitConnectNum = data.LimitConnectNum;
            peerData.MaxConnectNum = data.MaxConnectNum;
        }
    }

    public class MistPeerDataElement
    {
        public MistPeer Peer;
        public string Id;
        public Vector3 Position;
        public int CurrentConnectNum;
        public int MinConnectNum = 2;
        public int LimitConnectNum;
        public int MaxConnectNum;
        public MistPeerState State = MistPeerState.Disconnected;
        public float BlockConnectIntervalTime;  // 切断されたからの時間

        public MistPeerDataElement(string id)
        {
            Id = id;
            Peer = new(id);
        }
        public bool IsConnected => State == MistPeerState.Connected;
    }

    [Serializable]
    public class Chunk
    {
        public readonly static int Size = 16;
        private readonly static int ChunkSize = 3;
        private readonly static float DivideSize = 1.0f / Size;
        private static Chunk _previousChunk = new ();
        private static Chunk[] _surroundingChunks = new Chunk[ChunkSize * ChunkSize * ChunkSize];
        
        public int X = 0;
        public int Y = 0;
        public int Z = 0;
        public Chunk[] SurroundingChunks => _surroundingChunks;
        
        
        public Chunk()
        {
        }

        public Chunk(string chunkStr)
        {
            Set(chunkStr);
        }

        public bool ContainSurroundingChunk((int, int, int) chunk)
        {
            foreach (var surroundingChunk in _surroundingChunks)
            {
                if (surroundingChunk == null) continue;
                if (surroundingChunk.X == chunk.Item1 && surroundingChunk.Y == chunk.Item2 && surroundingChunk.Z == chunk.Item3)
                {
                    return true;
                }
            }

            return false;
        }

        public string Get()
        {
            return $"{X},{Y},{Z}";
        }

        public (int, int, int) GetTuple()
        {
            return (X, Y, Z);
        }

        public void Set(string data)
        {
            var splitStr = data.Split(",");
            X = int.Parse(splitStr[0]);
            Y = int.Parse(splitStr[1]);
            Z = int.Parse(splitStr[2]);
        }

        /// <summary>
        /// Chunkの更新
        /// </summary>
        /// <param name="position"></param>
        public bool Update(Vector3 position)
        {
            X = (int)(position.x * DivideSize);
            Y = (int)(position.y * DivideSize);
            Z = (int)(position.z * DivideSize);

            if (IsDifferentChunk())
            {
                _previousChunk = this;
                GetSurroundingChunks();
                return true;
            }

            return false;
        }

        private bool IsDifferentChunk()
        {
            return X != _previousChunk.X || Y != _previousChunk.Y || Z != _previousChunk.Z;
        }

        private void GetSurroundingChunks()
        {
            MistDebug.Log("GetSurroundingChunks");
            for(var x = 0; x < ChunkSize; x++)
            {
                for(var y = 0; y < ChunkSize; y++)
                {
                    for(var z = 0; z < ChunkSize; z++)
                    {
                        var index = x + y * ChunkSize + z * ChunkSize * ChunkSize;
                        _surroundingChunks[index] ??= new ();
                        _surroundingChunks[index].X = X + x - 1;
                        _surroundingChunks[index].Y = Y + y - 1;
                        _surroundingChunks[index].Z = Z + z - 1;
                    }
                }
            }
        }
    }
}