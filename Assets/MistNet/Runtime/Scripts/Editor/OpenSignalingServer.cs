using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace MistNet.Editor
{
    internal static class OpenSignalingServer
    {
        private static readonly string ParentDirectory = "MistNet";
        private static readonly string ServerFileName = "main.py";
            
        [MenuItem("Tools/Open SignalingServer")]
        private static void Execute()
        {
            Process.Start("code", $"{Application.dataPath}/../{ParentDirectory}");
        }

        [MenuItem("Tools/Run SignalingServer")]
        private static void RunServer()
        {
            string serverPath = $"{Application.dataPath}/../{ParentDirectory}/";
            Process.Start("powershell.exe", $"-Command \"cd {serverPath};python {ServerFileName}; Read-Host 'Press Enter to exit...'\"");
        }
    }
}