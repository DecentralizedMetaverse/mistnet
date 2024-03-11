using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
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
        public static string[] StunUrls { get; private set; } =
        {
            "stun:stun.l.google.com:19302",
        };
        public static Dictionary<int, float> RadiusAndSendIntervalSeconds = new()
        {
            { 3, 0.1f },
            { 6, 0.2f },
            { 12, 0.4f },
            { 24, 0.8f },
            { 48, 1.6f },
            { 96, 3.2f },
        };
        
        [Serializable]
        private class MistConfigData
        {
            public string SignalingServerAddress;
            public int MinConnection;
            public int LimitConnection;
            public int MaxConnection;
            public string[] StunUrls;
            public Dictionary<int, float> RadiusAndSendIntervalSeconds;
        }

        public void ReadConfig()
        {
            if (!File.Exists(ConfigPath)) return;
            var txt = File.ReadAllText(ConfigPath);
            var config = JsonConvert.DeserializeObject<MistConfigData>(txt);
            
            MinConnection = config.MinConnection;
            LimitConnection = config.LimitConnection;
            MaxConnection = config.MaxConnection;
            SignalingServerAddress = config.SignalingServerAddress;
            
            if(config.StunUrls is { Length: > 0 }) StunUrls = config.StunUrls;
            if(config.RadiusAndSendIntervalSeconds != null) RadiusAndSendIntervalSeconds = config.RadiusAndSendIntervalSeconds;
        }

        public void WriteConfig()
        {
            var config = new MistConfigData
            {
                MinConnection = MinConnection,
                LimitConnection = LimitConnection,
                MaxConnection = MaxConnection,  
                SignalingServerAddress = SignalingServerAddress,
                StunUrls = StunUrls,
                RadiusAndSendIntervalSeconds = RadiusAndSendIntervalSeconds,
            };
            // 整形表示で書き込み
            var txt = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ConfigPath,txt);
        }
    }
}