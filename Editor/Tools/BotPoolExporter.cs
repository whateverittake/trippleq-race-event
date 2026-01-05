#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Editor
{
    using TrippleQ.Event.RaceEvent.Runtime;
    public static class BotPoolExporter
    {
        private const string DefaultFileName = "race_bot_pool.json";

        [MenuItem("TrippleQ/Race Event/Export Bot Pool SO -> JSON")]
        public static void ExportSelected()
        {
            var so = Selection.activeObject as RaceBotPoolSO;
            if (so == null)
            {
                EditorUtility.DisplayDialog("Export Bot Pool", "Select a RaceBotPoolSO asset first.", "OK");
                return;
            }

            // Export to StreamingAssets so it can ship with build
            var dir = Path.Combine(Application.dataPath, "StreamingAssets");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, DefaultFileName);

            var pool = new BotPoolJson { Bots = so.Bots };
            var json = JsonUtility.ToJson(pool, prettyPrint: true);
            File.WriteAllText(path, json);

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Export Bot Pool", $"Exported to:\n{path}", "OK");
        }
    } 
}
#endif