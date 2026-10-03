using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>Prefixed wrappers around Unity logging so our lines are greppable in KSP.log.</summary>
    internal static class Log
    {
        private const string Prefix = "[KesslerSymptoms] ";

        public static void Info(string msg) { Debug.Log(Prefix + msg); }
        public static void Warn(string msg) { Debug.LogWarning(Prefix + msg); }
        public static void Error(string msg) { Debug.LogError(Prefix + msg); }
    }
}
