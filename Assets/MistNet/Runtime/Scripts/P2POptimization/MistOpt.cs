using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Unity.Plastic.Newtonsoft.Json;

namespace MistNet
{
    public class MistOpt : MonoBehaviour, IMistJoined, IMistLeft
    {
        private static readonly string FilePath = $"{Application.dataPath}/Data.json";
        private Dictionary<string, int> _currencyDict = new();
        private MistSyncObject _syncObject;
        public int myCurrency;
        private void Start()
        {
            _syncObject = GetComponent<MistSyncObject>();
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            _currencyDict = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
        }
        
        private void OnDestroy()
        {
            var json = JsonConvert.SerializeObject(_currencyDict);
            File.WriteAllText(FilePath, json);
        }

        public void OnJoined(string id)
        {
            Debug.Log($"OnJoined: {id}");
            _currencyDict.Add(id, 0);
        }

        public void OnLeft(string id)
        {
            Debug.Log($"OnLeft: {id}");
            _currencyDict.Remove(id);
        }
        
        public void AddCurrency()
        {
            var id = MistPeerData.I.SelfId;
            _syncObject.RPCAll(nameof(Add));
        }

        [MistRpc]
        private void Add(MessageInfo info)
        {
            if (!_currencyDict.ContainsKey(info.SenderId))
            {
                _currencyDict.Add(info.SenderId, 0);
            }
            _currencyDict[info.SenderId] += 100;
            _syncObject.RPC(info.SenderId, nameof(UpdateCurrency), _currencyDict[info.SenderId]);
        }

        [MistRpc]
        private void UpdateCurrency(int num)
        {
            myCurrency = num;
        }
    }
}