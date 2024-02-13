using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MistNet
{
    public class MistOptimizationData
    {
        // [Obsolete] public readonly Dictionary<(int, int, int), HashSet<string>> ChunkTable = new(); // key: chunk, value: id
        // [Obsolete] public readonly Dictionary<string, ChunkTableElement> ChunkTableElementDict = new(); // key: id, value: ChunkTableElement

        public readonly List<string> PeerConnectionPriorityList = new(); // id
        public readonly HashSet<string> PeerDisconnectRequestList = new(); // id
        public readonly Dictionary<string, int> DistanceDict = new(); // key: peerId, value: distance

        public MistOptimizationData()
        {
            PeerConnectionPriorityList.Clear();
            DistanceDict.Clear();
        }

        public void AddPeer(string id)
        {
            PeerConnectionPriorityList.Add(id);
        }

        public void RemovePeer(string id)
        {
            PeerConnectionPriorityList.Remove(id);
            MistSendingOptimizer.I.RemoveCategory(id);
        }

        public void UpdateDistance()
        {
            foreach (var id in PeerConnectionPriorityList)
            {
                var peer = MistManager.I.MistPeerData.GetPeer(id);
                if (peer == null) continue;

                if (!MistSyncManager.I.OwnerIdAndObjIdDict.TryGetValue(id, out var targetId)) continue;
                var targetObj = MistSyncManager.I.GetSyncObject(targetId);

                var selfObj = MistSyncManager.I.SelfSyncObject;

                var distance = Vector3.Distance(selfObj.transform.position, targetObj.transform.position);
                DistanceDict[id] = (int)distance;

                CategorizeClientByDistance(id, distance);
            }

            Sort();
        }

        private void Sort()
        {
            PeerConnectionPriorityList.Sort((a, b) =>
            {
                if (!DistanceDict.ContainsKey(a) || !DistanceDict.ContainsKey(b))
                {
                    return 128;
                }

                return DistanceDict[a].CompareTo(DistanceDict[b]);
            });
        }

        private void CategorizeClientByDistance(string id, float distance)
        {
            var category = distance switch
            {
                <= 3 => 3,
                <= 6 => 6,
                <= 12 => 12,
                <= 24 => 24,
                <= 48 => 48,
                _ => 96
            };

            MistSendingOptimizer.I.SetCategory(id, category);
        }
    }
}