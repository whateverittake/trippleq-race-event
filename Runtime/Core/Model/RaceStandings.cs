#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public static class RaceStandings
    {
        /// <summary>
        /// Sort:
        /// 1) LevelsCompleted desc
        /// 2) If both >= goal: FinishedUtcSeconds asc (finish sooner ranks higher)
        /// 3) Otherwise: LastUpdateUtcSeconds asc (reached that progress sooner ranks higher)
        /// </summary>

        public static List<RaceParticipant> Compute(IEnumerable<RaceParticipant> participants, int goalLevels)
        {

            return participants
                 .OrderByDescending(p => Math.Min(p.LevelsCompleted, goalLevels))
                 .ThenBy(p =>
                 {
                     var finished = (p.LevelsCompleted >= goalLevels) && p.HasFinished;
                     return finished ? p.FinishedUtcSeconds : p.LastUpdateUtcSeconds;
                 })
                 .ToList();
        }
    }
}
