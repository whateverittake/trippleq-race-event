
#nullable enable
namespace TrippleQ.Event.RaceEvent.Runtime
{
    public interface IRaceStorage
    {
        /// <summary>Load saved data. Return null if not found.</summary>
        RaceEventSave? Load();

        /// <summary>Save current data.</summary>
        void Save(RaceEventSave data);

        /// <summary>Clear all saved data for this feature.</summary>
        void Clear();
    }
}
