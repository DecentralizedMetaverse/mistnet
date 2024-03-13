using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace MistNet
{
    public class MistLogView : MonoBehaviour
    {
        private static readonly string LogFilePath = $"{Application.dataPath}/logs";
        
        [SerializeField] private int maxLogLines = 10;
        [SerializeField] private string filters = "";
        [SerializeField] private TMP_Text text;

        private string _log = "";
        private string _logAll = "";
        private List<Func<string, bool>> _filterFunctions;
        
        private void Start()
        {
            text.text = "";
            _filterFunctions = ParseFilters(filters);
            Application.logMessageReceived += OnLogMessageReceived;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            WriteLog();
        }
        
        private void OnValidate()
        {
            _filterFunctions = ParseFilters(filters);
        }

        private void OnLogMessageReceived(string condition, string stacktrace, LogType type)
        {
            if (!IsConditionMatched(condition)) return;
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _log += $"{condition}\n";
            _logAll += $"[{timestamp}] {condition}\n";
            var lines = _log.Split('\n');
            if (lines.Length > maxLogLines)
            {
                _log = string.Join("\n", lines, lines.Length - maxLogLines, maxLogLines);
            }

            text.text = _log;
        }

        private List<Func<string, bool>> ParseFilters(string filters)
        {
            var filterFunctions = new List<Func<string, bool>>();
            var filterGroups = filters.Split(new string[] { "or" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var group in filterGroups)
            {
                var conditions = group.Split(new string[] { "and" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(cond => cond.Trim())
                    .ToArray();
                filterFunctions.Add((condition) =>
                {
                    return conditions.All(cond => condition.Contains(cond));
                });
            }
            return filterFunctions;
        }

        private bool IsConditionMatched(string condition)
        {
            return _filterFunctions.Any(filterFunc => filterFunc(condition));
        }

        private void WriteLog()
        {
            if (!System.IO.Directory.Exists(LogFilePath))
            {
                System.IO.Directory.CreateDirectory(LogFilePath);
            }
            var path = $"{LogFilePath}/{DateTime.Now:yyyyMMdd_HHmmss}_{MistPeerData.I.SelfId}.txt";
            System.IO.File.WriteAllText(path, _logAll);
        }
    }

}