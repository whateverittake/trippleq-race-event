#nullable enable
namespace TrippleQ.Event.RaceEvent.Runtime
{
    public interface IKeyValueStorage
    {
        string Key { get; }

        /// <summary>Return null if not found.</summary>
        string? LoadString();

        void SaveString(string value);

        void Clear();
    }
}
