using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public static class JsonBotPoolLoader
    {
        private const string FileName = "race_bot_pool.json";
        public static string PersistentPath => Path.Combine(Application.persistentDataPath, FileName);
        public static string StreamingPath => Path.Combine(Application.streamingAssetsPath, FileName);

        public static BotPoolJson LoadOrFallback()
        {
            if (File.Exists(PersistentPath))
                return FromText(File.ReadAllText(PersistentPath));

            if (File.Exists(StreamingPath))
            {
                // copy to persistent for future hotfix override
                try { File.Copy(StreamingPath, PersistentPath); } catch { }

                // read streaming as fallback
                return FromText(File.ReadAllText(StreamingPath));
            }

            return new BotPoolJson();
        }

        private static BotPoolJson FromText(string json)
        {
            try { return JsonUtility.FromJson<BotPoolJson>(json) ?? new BotPoolJson(); }
            catch { return new BotPoolJson(); }
        }

        public static IEnumerator LoadOrFallbackAsync(Action<BotPoolJson> onDone)
        {
            // 1. Ưu tiên persistent (hotfix)
            if (File.Exists(PersistentPath))
            {
                onDone(FromText(File.ReadAllText(PersistentPath)));
                yield break;
            }

            // 2. Load từ StreamingAssets
#if UNITY_ANDROID && !UNITY_EDITOR
    using (var req = UnityEngine.Networking.UnityWebRequest.Get(StreamingPath))
    {
        yield return req.SendWebRequest();

        if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            var json = req.downloadHandler.text;

            // copy sang persistent để lần sau dùng File IO
            try { File.WriteAllText(PersistentPath, json); } catch { }

            onDone(FromText(json));
            yield break;
        }
    }
#else
            if (File.Exists(StreamingPath))
            {
                var json = File.ReadAllText(StreamingPath);
                try { File.WriteAllText(PersistentPath, json); } catch { }
                onDone(FromText(json));
                yield break;
            }
#endif

            // 3. fallback cuối
            onDone(new BotPoolJson());
        }

    }
}
