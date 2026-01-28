using System;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed partial class RaceEventService
    {
        public DateTime GetNextResetLocal(DateTime localNow)
        {
            // reset hour like 4:00
            var resetToday = new DateTime(localNow.Year, localNow.Month, localNow.Day, ActiveConfigForRunOrCursor().ResetHourLocal, 0, 0);

            // if we're already past today's reset => next reset is tomorrow
            if (localNow >= resetToday)
                return resetToday.AddDays(1);

            // else next reset is today at reset hour
            return resetToday;
        }

        public RaceHudStatus GetHudStatus(DateTime localNow)
        {
            ThrowIfNotInitialized();

            var cfg = ActiveConfigForRunOrCursor();
            //Log("xx 1");
            // if feature off -> hide
            if (!cfg.Enabled)
                return new RaceHudStatus(false, false, false, TimeSpan.Zero, "Next: ", false);

            //HUD preview lock window ---
            const int previewOffset = 5;
            int unlockLevel = Math.Max(0, 20);
            int showPreviewLevel = Math.Max(0, unlockLevel - previewOffset);

            // before preview => hide widget
            if (CurrentLevel < showPreviewLevel)
                return new RaceHudStatus(false, false, false, TimeSpan.Zero, "", false);

            // preview but locked
            if (CurrentLevel < unlockLevel)
            {
                return new RaceHudStatus(
                    isVisible: true,
                    isSleeping: true,          // dùng icon xám/sleeping state
                    hasClaim: false,
                    remaining: TimeSpan.Zero,
                    label: $"Lvl {unlockLevel}",
                    showTextCountdown: false,
                    isLocked: true,
                    unlockAtLevel: unlockLevel
                );
            }
            //Log("xx 2: "+ State);
            // If ended & can claim => show claim attention (not sleeping)
            if (State == RaceEventState.Ended && CanClaim())
                return new RaceHudStatus(true, false, true, TimeSpan.Zero, "Claim now!", false);
            //Log("xx 3");
            // If in race -> hide widget (hoặc show active icon)
            if (State == RaceEventState.InRace || State == RaceEventState.Searching)
            {
                // no run? fallback hide
                if (_run == null)
                    return new RaceHudStatus(false, false, false, TimeSpan.Zero, "", false);

                // (A) EXTENDED: show remaining to EndUtc (≈ 1h),
                // and if expired -> hide (your requirement)
                if (_run.HasExtended)
                {
                    var nowUtc = NowUtcSeconds();
                    var remainingSec = _run.EndUtcSeconds - nowUtc;

                    if (remainingSec <= 0)
                        return new RaceHudStatus(false, false, false, TimeSpan.Zero, "", false);

                    var remaining = TimeSpan.FromSeconds(remainingSec);
                    return new RaceHudStatus(true, false, false, remaining, "End in: ", true);
                }

                // (B) NOT EXTENDED: always count down to next 4AM reset
                var nextReset = GetNextResetLocal(localNow); // uses ResetHourLocal
                var remaining2 = nextReset - localNow;
                if (remaining2 < TimeSpan.Zero) remaining2 = TimeSpan.Zero;

                return new RaceHudStatus(true, false, false, remaining2, "End in: ", true);
            }
            //Log("xx 4");
            // Otherwise: idle/eligible -> if eligible you may show active icon, if not eligible show sleeping + countdown
            var canShowEntry = ShouldShowEntryPopup(isInTutorial: false, localNow); // HUD không biết tutorial thì bạn có thể truyền vào overload khác
            if (canShowEntry)
            {
                // active state: no countdown
                return new RaceHudStatus(true, false, false, TimeSpan.Zero, "Race Event", false);
            }
            //Log("xx 5");
            var nextReset3 = GetNextResetLocal(localNow);
            var remaining3 = nextReset3 - localNow;
            if (remaining3 < TimeSpan.Zero) remaining3 = TimeSpan.Zero;

            return new RaceHudStatus(true, true, false, remaining3, "Next in: ", true);
        }

        public void RequestInRacePopup()
        {
            ThrowIfNotInitialized();
            if (State == RaceEventState.InRace)
                RequestPopup(new PopupRequest(PopupType.Main));
        }

        public void RequestEndedPopup()
        {
            ThrowIfNotInitialized();
            if (State == RaceEventState.Ended || State == RaceEventState.ExtendOffer)
                RequestPopup(new PopupRequest(PopupType.Ended));
        }

        public void RequestEntryPopup(bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();
            RefreshEligibility(isInTutorial, localNow);
            if (ShouldShowEntryPopup(isInTutorial, localNow))
                RequestPopup(new PopupRequest(PopupType.Entry));
        }

        public void ForceRequestEntryPopup(bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();
            RefreshEligibility(isInTutorial, localNow);
            RequestPopup(new PopupRequest(PopupType.Entry));
        }

        public RaceHudClickAction GetHudClickAction(bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();

            return GetHudMode(isInTutorial, localNow) switch
            {
                HudMode.Claim => RaceHudClickAction.OpenEnded,
                HudMode.InRace => RaceHudClickAction.OpenInRace,
                HudMode.Entry => RaceHudClickAction.OpenEntry,
                _ => RaceHudClickAction.None
            };

            //var hud = GetHudStatus(localNow);
            //if (hud.IsLocked) return RaceHudClickAction.None;

            //if (State == RaceEventState.Ended && CanClaim())
            //    return RaceHudClickAction.OpenEnded;

            //if (State == RaceEventState.InRace)
            //    return RaceHudClickAction.OpenInRace;

            //if(State == RaceEventState.ExtendOffer)
            //    return RaceHudClickAction.OpenEnded;

            //if (ShouldShowEntryPopup(isInTutorial, localNow))
            //    return RaceHudClickAction.OpenEntry;

            //return RaceHudClickAction.None;
        }

        public string FormatHMS(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            int h = (int)t.TotalHours;
            int m = t.Minutes;
            int s = t.Seconds;
            return $"{h:00}:{m:00}:{s:00}";
        }

        public string FormatHM(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;

            // làm tròn lên theo phút
            var totalMinutes = (int)Math.Ceiling(t.TotalMinutes);

            int h = totalMinutes / 60;
            int m = totalMinutes % 60;

            return $"{h}h{m:00}'";
        }

        public LeaderboardSnapshot GetLeaderboardSnapshot(int topN)
        {
            ThrowIfNotInitialized();
            if (_run == null) return LeaderboardSnapshot.Empty();

            // 1) lấy standings theo cùng 1 rule duy nhất
            // nếu RaceStandings.Compute đã sort đúng theo rule finishUtc / progress thì dùng lại luôn
            var standings = RaceStandings.Compute(_run.AllParticipants(), _run.GoalLevels);

            // 2) lấy rank player
            int playerRank = standings.FindIndex(p => p.Id == _run.Player.Id) + 1;
            if (playerRank <= 0) playerRank = standings.Count;

            // 3) topN
            var top = standings.Count <= topN ? standings : standings.GetRange(0, topN);

            return new LeaderboardSnapshot(top, playerRank, _run.Player.HasFinished);
        }

        public string ExtractNumberSuffix(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            int i = input.Length - 1;
            while (i >= 0 && char.IsDigit(input[i]))
                i--;

            return input.Substring(i + 1);
        }

        private HudMode GetHudMode(bool isInTutorial, DateTime localNow)
        {
            var hud = GetHudStatus(localNow);

            // 1) HUD-driven first
            if (!hud.IsVisible) return HudMode.Hidden;
            if (hud.IsLocked) return HudMode.Locked;

            // 2) State overrides (business meaning)
            if (State == RaceEventState.Ended && CanClaim())
                return HudMode.Claim;

            if (State == RaceEventState.ExtendOffer)
                return HudMode.Claim;

            if (State == RaceEventState.InRace)
                return HudMode.InRace;

            // 3) Entry gate (Idle/others)
            if (ShouldShowEntryPopup(isInTutorial, localNow))
                return HudMode.Entry;

            return HudMode.Sleeping;
        }
    }
}
