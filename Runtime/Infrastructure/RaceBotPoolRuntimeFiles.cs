using UnityEngine;
using System.IO;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public static class RaceBotPoolRuntimeFiles
    {
        public const string FileName = "race_bot_pool.json";

        public static string PersistentPath => Path.Combine(Application.persistentDataPath, FileName);
        public static string StreamingPath => Path.Combine(Application.streamingAssetsPath, FileName);

        public static void EnsurePersistent()
        {
            if (File.Exists(PersistentPath)) return;

            // StreamingAssets read
            if (File.Exists(StreamingPath))
            {
                File.Copy(StreamingPath, PersistentPath);
            }
            else
            {
                Debug.LogWarning($"Bot pool json not found at StreamingAssets: {StreamingPath}");
            }
        }
    }
}
