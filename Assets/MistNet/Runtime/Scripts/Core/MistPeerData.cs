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
                if (peerData.Peer == null) peerData.Peer = new MistPeer(id);
                else peerData.Peer.Id = id;
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
            if (!_dict.TryGetValue(id, out var peerData)) return null;
            peerData.Peer ??= new(id); // TODO: ここでPeerを生成しても大丈夫か
            return peerData;
        }

        public void SetState(string id, MistPeerState state)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!_dict.TryGetValue(id, out var peerData)) return;
            peerData.State = state;
            if (state == MistPeerState.Disconnected)
            {
                peerData.Peer = null;
            }
        }

        public bool UpdatePeerData(string id, P_PeerData data)
        {
            if (string.IsNullOrEmpty(id)) return false;

            _dict.TryAdd(id, new MistPeerDataElement(id));

            
            var peerData = _dict[id];
            peerData.Peer ??= new(id);
            peerData.Id = id;
            peerData.Peer.Id = id;
            peerData.Position = data.Position;
            peerData.CurrentConnectNum = data.CurrentConnectNum;
            peerData.MinConnectNum = data.MinConnectNum;
            peerData.LimitConnectNum = data.LimitConnectNum;
            peerData.MaxConnectNum = data.MaxConnectNum;

            // return peerData.State != MistPeerState.Connected;
            return true;
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
        public float Distance { get; set; }
        public int BlockConnectIntervalTime { get; set; }
        public MistPeerDataElement(string id)
        {
            Id = id;
            Peer = new(id);
        }

        public bool IsConnected => State == MistPeerState.Connected;
    }
}