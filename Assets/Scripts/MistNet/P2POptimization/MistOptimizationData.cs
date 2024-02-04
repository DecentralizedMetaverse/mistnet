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

        // [Obsolete]
        // public struct ChunkTableElement
        // {
        //     public (int, int, int) Chunk;
        //     public string Id;
        //     public Vector3 Position;
        //     public int CurrentConnectNum;
        //     public int MinConnectNum;
        //     public int LimitConnectNum;
        //     public int MaxConnectNum;
        //     public MistPeerState State;
        // }

        public MistOptimizationData()
        {
            PeerConnectionPriorityList.Clear();
            DistanceDict.Clear();
        }

        // public void UpdateChunkDict((int, int, int) chunk, ChunkTableElement data)
        // {
        //     Debug.Log("[Debug] UpdateChunkDict");
        //     if (!ChunkTable.ContainsKey(chunk))
        //     {
        //         ChunkTable.Add(chunk, new());
        //     }
        //
        //     ChunkTable[chunk].Add(data.Id);
        //
        //     if (ChunkTableElementDict.ContainsKey(data.Id))
        //     {
        //         ChunkTableElementDict[data.Id] = data;
        //         return;
        //     }
        //     ChunkTableElementDict.Add(data.Id, data);
        // }
        
        // public void RemoveFromId(string id){
        //     if (!ChunkTableElementDict.ContainsKey(id)) return;
        //     var chunk = ChunkTableElementDict[id].Chunk;
        //     ChunkTable[chunk].Remove(id);
        //     ChunkTableElementDict.Remove(id);
        // }

        /// <summary>
        /// Selfと同じChunkかどうか
        /// </summary>
        /// <param name="chunkStr"></param>
        /// <returns></returns>
        public bool IsSameChunkWithSelf(string chunkStr)
        {
            var selfChunk = MistSyncManager.I.SelfSyncObject.Chunk.Get();
            if (chunkStr == selfChunk) return true;
            return false;
        }

        // public (int, int, int) GetNearChunk((int, int, int) chunk)
        // {
        //     if (ChunkTable.ContainsKey(chunk)) return chunk;
        //
        //     Dictionary<(int, int, int), int> chunkDistanceList = new();
        //     foreach (var ch2 in ChunkTable.Keys)
        //     {
        //         var distance = Vector3.Distance(new Vector3(chunk.Item1, chunk.Item2, chunk.Item3),
        //             new Vector3(ch2.Item1, ch2.Item2, ch2.Item3));
        //         chunkDistanceList.Add(ch2, (int)distance);
        //     }
        //
        //     // Sort
        //     var sortedChunkDistanceList = chunkDistanceList.OrderBy((x) => x.Value);
        //     return sortedChunkDistanceList.First().Key;
        // }

        // 隣接Chunkかどうか
        public bool IsNeighborChunk((int, int, int) chunk)
        {
            var selfChunkStr = MistSyncManager.I.SelfSyncObject.Chunk.Get();
            var selfChunk = selfChunkStr.Split(",").Select(int.Parse).ToArray();
            var distance = Vector3.Distance(new Vector3(chunk.Item1, chunk.Item2, chunk.Item3),
                new Vector3(selfChunk[0], selfChunk[1], selfChunk[2]));

            return distance <= 1;
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