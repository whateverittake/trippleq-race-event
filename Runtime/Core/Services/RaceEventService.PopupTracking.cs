using System.Collections.Generic;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed partial class RaceEventService
    {
        public void NotifyPopupShown(PopupType type)
        {
            _currentPopupTypeOpen = type;
            //Log($"[DEBUG] NotifyPopupShown.{type}");
            //if (ConsumeFirstTimePopup(type))
            //{
            //    Log($"[Tutorial] First time popup shown: {type}");
            //    OnTutorialRequested?.Invoke(type);
            //}
        }

        public void NotifyPopupHidden(PopupType type)
        {
            if (_currentPopupTypeOpen == type)
                _currentPopupTypeOpen = null;
        }

        private bool IsPopupActive(PopupType type)
        {
            return _currentPopupTypeOpen == type;
        }

        public bool ConsumeFirstTimePopup(PopupType type)
        {
            ThrowIfNotInitialized();

            _save.SeenPopupTypes ??= new List<int>();

            int key = (int)type;
            for (int i = 0; i < _save.SeenPopupTypes.Count; i++)
                if (_save.SeenPopupTypes[i] == key)
                    return false;

            // first time ever
            _save.SeenPopupTypes.Add(key);
            TrySave();
            return true;
        }

        public void RemoveConsumePopup(PopupType type)
        {
            ThrowIfNotInitialized();
            _save.SeenPopupTypes ??= new List<int>();

            int key = (int)type;

            if (_save.SeenPopupTypes.Contains(key))
            {
                _save.SeenPopupTypes.Remove(key);
            }
        }
    }
}
