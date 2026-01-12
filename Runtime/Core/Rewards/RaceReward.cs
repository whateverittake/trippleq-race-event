using System;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    [Serializable]
    public sealed class RaceReward
    {
        public int Gold;
        public int Gems;
        public int Booster1;
        public int Booster2;
        public int Booster3;
        public int Booster4;

        public RaceReward(int gold, int gems, int booster1, int booster2, int booster3, int booster4)
        {
            Gold = gold;
            Gems = gems;
            Booster1 = booster1;
            Booster2 = booster2;
            Booster3 = booster3;
            Booster4 = booster4;
        }
    }
}
