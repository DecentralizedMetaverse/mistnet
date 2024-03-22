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
        private static MistConfigData _config;

        public static int MinConnection => _config.minConnection;
        public static int LimitConnection => _config.limitConnection;
        public static int MaxConnection => _config.maxConnection;
        public static string SignalingServerAddress => _config.signalingServerAddress;
        public static string[] StunUrls => _config.stunUrls;
        public static Dictionary<int, float> RadiusAndSendIntervalSeconds => _config.radiusAndSendIntervalSeconds;
        public static int LatencyMilliseconds => _config.latencyMilliseconds;
        public static float IntervalSendTableTimeSeconds => _config.intervalSendTableTimeSeconds;
        public static bool DebugLog => _config.debugLog;
        public static string LogFilter => _config.logFilter;
        public static int ShowLogLine => _config.showLogLine;

        [Serializable]
        private class MistConfigData
        {
            public string signalingServerAddress = "ws://localhost:8080/ws";
            public int minConnection = 3;
            public int limitConnection = 20;
            public int maxConnection = 80;
            public string[] stunUrls = { "stun:stun.l.google.com:19302" };
            public Dictionary<int, float> radiusAndSendIntervalSeconds = new()
            {
                { 3, 0.05f },
                { 6, 0.1f },
                { 12, 0.2f },
                { 24, 0.25f },
                { 48, 0.5f },
                { 96, 1.0f },
            };
            public int latencyMilliseconds = 0;
            public float intervalSendTableTimeSeconds = 1.5f;
            public bool debugLog = false;
            public string logFilter = "[STATS]"; // ログフィルターの種類を指定する文字列
            public int showLogLine = 10;
        }

        public void ReadConfig()
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                _config = JsonConvert.DeserializeObject<MistConfigData>(json);
            }
            else
            {
                _config = new MistConfigData();
                WriteConfig();
            }
        }

        private void WriteConfig()
        {
            var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }
    }
}