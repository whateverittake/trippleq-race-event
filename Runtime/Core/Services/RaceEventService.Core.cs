using System;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed partial class RaceEventService
    {
        // --------------------
        // Core helpers
        // --------------------
        internal long NowUtcSeconds()
        {
            // Normal: strict UTC.
            // Test: use device local clock (affected by changing local time).
            return IsInTestMode
                ? DateTimeOffset.Now.ToUnixTimeSeconds()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        internal DateTime NowLocal() => DateTime.Now;

        internal long NowLocalUnixSeconds() => DateTimeOffset.Now.ToUnixTimeSeconds();

        internal void TrySave()
        {
            try
            {
                _storage.Save(_save);
            }
            catch (Exception e)
            {
                Log("Save failed: " + e.Message);
            }
        }

        private void PublishRunUpdated()
        {
            OnRunUpdated?.Invoke(_run);
        }

        public void RequestPopup(PopupType type)
        {
            RequestPopup(new PopupRequest(type));
        }

        private void RequestPopup(PopupRequest type) => OnPopupRequested?.Invoke(type);

        internal void Log(string msg) => OnLog?.Invoke(msg);

        internal void ThrowIfNotInitialized()
        {
            ThrowIfDisposed();
            if (!_initialized) throw new InvalidOperationException("RaceEventService not initialized. Call Initialize(initialLevel).");
        }


        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RaceEventService));
        }

    }
}
