namespace TrippleQ.Event.RaceEvent.Runtime
{
    public enum RaceEventState
    {
        Disabled,
        Idle,
        Eligible,
        Searching,
        InRace,
        Ended,
        Claimable,
        Cooldown,
        ExtendOffer
    }
}
