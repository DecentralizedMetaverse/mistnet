using System;
using System.IO;
using UnityEngine;

namespace MistNet
{
    public class MistConfig
    {
        private static readonly string ConfigPath = $"{Application.dataPath}/../MistNetConfig.json";
        public static int MinConnection { get; private set; } = 3;
        public static int LimitConnection { get; private set; } = 20;
        public static int MaxConnection { get; private set; } = 80;
        public static string SignalingServerAddress { get; private set; } = "ws://localhost:8080/ws";
        
        [Serializable]
        private class MistConfigData
        {
            public string SignalingServerAddress;
            public int MinConnection;
            public int LimitConnection;
            public int MaxConnection;
        }

        public void ReadConfig()
        {
            if (!File.Exists(ConfigPath)) return;
            var txt = File.ReadAllText(ConfigPath);
            var config = JsonUtility.FromJson<MistConfigData>(txt);
            MinConnection = config.MinConnection;
            LimitConnection = config.LimitConnection;
            MaxConnection = config.MaxConnection;
            SignalingServerAddress = config.SignalingServerAddress;
        }

        public void WriteConfig()
        {
            var config = new MistConfigData
            {
                MinConnection = MinConnection,
                LimitConnection = LimitConnection,
                MaxConnection = MaxConnection,  
                SignalingServerAddress = SignalingServerAddress
            };
            // 整形表示で書き込み
            var txt = JsonUtility.ToJson(config, true);
            File.WriteAllText(ConfigPath,txt);
        }
    }
}