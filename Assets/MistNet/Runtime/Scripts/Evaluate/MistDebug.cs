using UnityEngine;

namespace MistNet
{

    public static class MistDebug
    {
        public static bool ShowLog = true;
        public static void Log(object message)
        {
            if (!ShowLog) return;
            Debug.Log(message);
        }
        
        public static void LogWarning(object message)
        {
            if (!ShowLog) return;
            Debug.LogWarning(message);
        }
        
        public static void LogError(object message)
        {
            if (!ShowLog) return;
            Debug.LogError(message);
        }
    }
}
