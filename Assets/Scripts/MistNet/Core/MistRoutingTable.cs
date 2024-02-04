using System.Collections.Generic;
using UnityEngine;

namespace MistNet
{
    public class MistRoutingTable
    {
        private readonly Dictionary<string, string> _routingTable = new();

        public void Add(string sourceId, string fromId)
        {
            if (sourceId == MistManager.I.MistPeerData.SelfId) return;
            if (sourceId == fromId) return;
            
            Debug.Log($"[RoutingTable] Add {sourceId} from {fromId}");
            if (!_routingTable.ContainsKey(sourceId))
            {
                _routingTable.Add(sourceId, fromId);
                return;
            }

            _routingTable[sourceId] = fromId;
        }

        public string Get(string targetId)
        {
            Debug.Log($"[RoutingTable] Get {targetId}");
            if (_routingTable.TryGetValue(targetId, out var value))
            {
                return value;
            }

            return "";
        }
    }
}