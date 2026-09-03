using System;

namespace RPGMapGeneration.Diagnostics
{
    /// <summary>
    /// Replacement for <c>UnityEngine.Debug.Log</c>. By default the library is silent; hook
    /// <see cref="Info"/> up to Unity's logger, a console writer or a test sink as needed.
    /// </summary>
    public static class MapLog
    {
        /// <summary>Receives informational messages, for example when a texture was written.</summary>
        public static Action<string>? Info { get; set; }

        internal static void Log(string message)
        {
            Info?.Invoke(message);
        }
    }
}
