using UnityEngine;

namespace MistNet
{

    public static class MistDebug
    {
        public static void Log(object message)
        {
            if (!MistConfig.DebugLog) return;
            Debug.Log(message);
        }
        
        public static void LogWarning(object message)
        {
            if (!MistConfig.DebugLog) return;
            Debug.LogWarning(message);
        }
        
        public static void LogError(object message)
        {
            if (!MistConfig.DebugLog) return;
            Debug.LogError(message);
        }
    }
}
