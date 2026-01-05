#nullable enable
using System;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    /// <summary>
    /// Typed storage that serializes RaceEventSave to JSON string.
    /// Backing store can be File/PlayerPrefs/Redis later via IKeyValueStorage.
    /// </summary>
    public sealed class JsonRaceStorage : IRaceStorage
    {
        private readonly IKeyValueStorage _kv;

        [Serializable]
        private class Wrapper
        {
            public int Version = 1;
            public RaceEventSave Save;
        }

        public JsonRaceStorage(IKeyValueStorage kv)
        {
            _kv = kv ?? throw new ArgumentNullException(nameof(kv));
        }

        public void Clear()=> _kv.Clear();

        public RaceEventSave? Load()
        {
            var json = _kv.LoadString();
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                var wrapper = JsonUtility.FromJson<Wrapper>(json);
                return wrapper?.Save;
            }
            catch
            {
                return null;
            }
        }

        public void Save(RaceEventSave data)
        {
            var wrapper = new Wrapper
            {
                Version = 1,
                Save = data
            };

            var json = JsonUtility.ToJson(wrapper, prettyPrint: true);
            _kv.SaveString(json);
        }
    }
}
