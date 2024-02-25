using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MistNet
{
    public class MistOptimizationData
    {
        public readonly List<string> PeerConnectionPriorityList = new(); // id
        public readonly HashSet<string> PeerDisconnectRequestList = new(); // id
        public readonly Dictionary<string, int> DistanceDict = new(); // key: peerId, value: distance
        private readonly List<int> _radiusAndSendInterval;

        public MistOptimizationData()
        {
            // RadiusAndSendIntervalのキーを距離が小さい順にソート
            _radiusAndSendInterval = new List<int>(MistConfig.RadiusAndSendIntervalSeconds.Keys);
            _radiusAndSendInterval.Sort();
            
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
            var category = _radiusAndSendInterval.Last(); // 一番遠い距離のカテゴリー
            foreach (var key in _radiusAndSendInterval.Where(key => distance <= key))
            {
                category = key;
                break;
            }

            MistSendingOptimizer.I.SetCategory(id, category);
        }
    }
}