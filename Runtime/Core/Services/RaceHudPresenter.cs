using System;
using static TrippleQ.Event.RaceEvent.Runtime.RaceHudPresenter;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    /// <summary>
    /// RaceHudPresenter: chỉ làm nhiệm vụ "map" từ context (data/state/time) -> RaceHudStatus.
    /// KHÔNG gọi ngược lại RaceEventService, KHÔNG Save/Publish, KHÔNG đụng scheduler/engine.
    /// </summary>
    public sealed class RaceHudPresenter
    {
        private const string LABEL_EMPTY = "";
        private const string LABEL_CLAIM_NOW = "Claim now!";
        private const string LABEL_END_IN = "End in: ";
        private const string LABEL_NEXT_IN = "Next in: ";
        private const string LABEL_RACE_EVENT = "Race Event";
        private const string LABEL_LVL_PREFIX = "Lvl ";
        private const string LABEL_START_NEXT = "Start next!";

        /// <summary>
        /// Context tối thiểu để tính HUD.
        /// Service chịu trách nhiệm build context này.
        /// </summary>
        public readonly struct RaceHudContext
        {
            public readonly DateTime LocalNow;
            public readonly long UtcNow;

            public readonly int CurrentLevel;
            public readonly RaceEventState State;

            public readonly RaceEventConfig Cfg;
            public readonly RaceRun? Run;

            public readonly bool CanClaim;
            public readonly bool IsEligible;
            public readonly bool CanStartNextRoundNow;

            public readonly DateTime NextResetLocal;

            public RaceHudContext(
                DateTime localNow,
                long utcNow,
                int currentLevel,
                RaceEventState state,
                RaceEventConfig cfg,
                RaceRun? run,
                bool canClaim,
                bool isEligible,
                DateTime nextResetLocal,
                bool canStartNextRoundNow)
            {
                LocalNow = localNow;
                UtcNow = utcNow;
                CurrentLevel = currentLevel;
                State = state;
                Cfg = cfg;
                Run = run;
                CanClaim = canClaim;
                CanStartNextRoundNow = canStartNextRoundNow;
                IsEligible = isEligible;
                NextResetLocal = nextResetLocal;
            }
        }

        /// <summary>
        /// Từ context hiện tại quyết định HUD:
        /// - có hiện không
        /// - đang sleeping hay active
        /// - có claim không
        /// - countdown còn bao lâu (end in / next in)
        /// - locked preview (unlock level)
        /// </summary>
        public RaceHudStatus BuildHudStatus(in RaceHudContext ctx)
        {
            var cfg = ctx.Cfg;

            // Feature off => hide HUD
            if (!cfg.Enabled)
                return new RaceHudStatus(false, false, false, TimeSpan.Zero, LABEL_EMPTY, false);

            // ----- HUD preview lock window -----
            // NOTE: tạm thời giữ rule giống bạn: previewOffset=5, unlockLevel=20.
            // Sau này nên lấy unlockLevel từ config (vd cfg.UnlockAtLevel).
            const int previewOffset = 5;
            int unlockLevel = Math.Max(0, 20);
            int showPreviewLevel = Math.Max(0, unlockLevel - previewOffset);

            // Trước ngưỡng preview => hide widget
            if (ctx.CurrentLevel < showPreviewLevel)
                return new RaceHudStatus(false, false, false, TimeSpan.Zero, LABEL_EMPTY, false);

            // Preview nhưng locked
            if (ctx.CurrentLevel < unlockLevel)
            {
                return new RaceHudStatus(
                    isVisible: true,
                    isSleeping: true,
                    hasClaim: false,
                    remaining: TimeSpan.Zero,
                    label: LABEL_LVL_PREFIX + unlockLevel,
                    showTextCountdown: false,
                    isLocked: true,
                    unlockAtLevel: unlockLevel
                );
            }

            // Ended + claim
            if (ctx.State == RaceEventState.Ended && ctx.CanClaim)
                return new RaceHudStatus(true, false, true, TimeSpan.Zero, LABEL_CLAIM_NOW, false);

            // Ended & đã claim & đủ gap => cho start round kế (round 2/3)
            if (ctx.State == RaceEventState.Ended && !ctx.CanClaim && ctx.CanStartNextRoundNow)
                return new RaceHudStatus(true, false, false, TimeSpan.Zero, LABEL_START_NEXT, false);

            // Ended & đã claim nhưng chưa đủ gap => show countdown tới next start
            if (ctx.State == RaceEventState.Ended && ctx.Run != null && ctx.Run.HasClaimed)
            {
                var nextUtc = ctx.Run.NextAllowedStartUtcSeconds;
                if (nextUtc > 0 && ctx.UtcNow < nextUtc)
                {
                    var rem = TimeSpan.FromSeconds(nextUtc - ctx.UtcNow);
                    return new RaceHudStatus(true, true, false, rem, LABEL_NEXT_IN, true);
                }
            }

            // InRace/Search => show countdown "End in"
            if (ctx.State == RaceEventState.InRace || ctx.State == RaceEventState.Searching)
            {
                var run = ctx.Run;
                if (run == null)
                    return new RaceHudStatus(false, false, false, TimeSpan.Zero, LABEL_EMPTY, false);

                TimeSpan remaining;
                // vNext: InRace/Search countdown theo Run.EndUtc (round 0/1: +8h, round2: tới reset)
                var endUtc = run.EndUtcSeconds;
                if (endUtc > 0)
                {
                    remaining = TimeSpan.FromSeconds(Math.Max(0, endUtc - ctx.UtcNow));
                }
                else
                {
                    remaining = ctx.NextResetLocal - ctx.LocalNow;
                }

                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

                return new RaceHudStatus(true, false, false, remaining, LABEL_END_IN, true);
            }

            // Idle/Eligible: nếu có thể entry => active icon
            if (ctx.IsEligible)
                return new RaceHudStatus(true, false, false, TimeSpan.Zero, LABEL_RACE_EVENT, false);

            // Không entry được => sleeping + countdown tới next reset
            var remaining2 = ctx.NextResetLocal - ctx.LocalNow;
            if (remaining2 < TimeSpan.Zero) remaining2 = TimeSpan.Zero;

            return new RaceHudStatus(true, true, false, remaining2, LABEL_NEXT_IN, true);
        }

        /// <summary>
        /// BuildHudMode:
        /// - Ưu tiên theo HUD status trước (Hidden/Locked).
        /// - Sau đó override theo State (Claim/InRace).
        /// - Cuối cùng mới xét entry gate (CanShowEntry) để ra Entry/Sleeping.
        /// </summary>
        public HudMode BuildHudMode(in RaceHudContext ctx, in RaceHudStatus hud)
        {
            // 1) HUD-driven overrides (do view quyết định)
            if (!hud.IsVisible)
                return HudMode.Hidden;

            if (hud.IsLocked)
                return HudMode.Locked;

            // 2) State overrides (business meaning)
            // NOTE: giữ đúng logic cũ:
            // - Ended & can claim => Claim
            // - InRace => InRace
            if (ctx.State == RaceEventState.Ended && ctx.CanClaim)
                return HudMode.Claim;

            if (ctx.State == RaceEventState.Ended && !ctx.CanClaim && ctx.CanStartNextRoundNow)
                return HudMode.StartNext;

            if (ctx.State == RaceEventState.InRace)
                return HudMode.InRace;

            // 3) Entry gate (Idle/others)
            if (ctx.IsEligible)
                return HudMode.Entry;

            return HudMode.Sleeping;
        }

        /// <summary>
        /// Map mode -> hành động khi click HUD:
        /// Claim => OpenEnded
        ///  InRace => OpenInRace
        ///  Entry => OpenEntry
        ///  khác => None
        ///  </summary>
        public RaceHudClickAction BuildHudClickAction(HudMode mode)
        {
            return mode switch
            {
                HudMode.Claim => RaceHudClickAction.OpenEnded,
                HudMode.StartNext => RaceHudClickAction.OpenEnded,
                HudMode.InRace => RaceHudClickAction.OpenInRace,
                HudMode.Entry => RaceHudClickAction.OpenEntry,
                _ => RaceHudClickAction.None
            };
        }
    }

    public readonly struct RaceHudStatus
    {
        public readonly bool IsVisible;
        public readonly bool IsSleeping;     // icon xám + zzz
        public readonly bool HasClaim;       // show "!" rung / claim now
        public readonly TimeSpan Remaining;  // countdown text
        public readonly string Label;        // optional: "NEXT RACE"
        public readonly bool ShowTextCountdown;

        public readonly bool IsLocked;
        public readonly int UnlockAtLevel;

        public RaceHudStatus(bool isVisible, bool isSleeping, bool hasClaim, TimeSpan remaining, string label, bool showTextCountdown)
         : this(isVisible, isSleeping, hasClaim, remaining, label, showTextCountdown, isLocked: false, unlockAtLevel: 0) { }

        public RaceHudStatus(bool isVisible, bool isSleeping, bool hasClaim, TimeSpan remaining, string label, bool showTextCountdown,
                        bool isLocked, int unlockAtLevel)
        {
            IsVisible = isVisible;
            IsSleeping = isSleeping;
            HasClaim = hasClaim;
            Remaining = remaining;
            Label = label;
            ShowTextCountdown = showTextCountdown;

            IsLocked = isLocked;
            UnlockAtLevel = unlockAtLevel;
        }
    }

    public enum RaceHudClickAction
    {
        None,
        OpenEntry,
        OpenInRace,
        OpenEnded
    }

    public enum HudMode
    {
        Hidden,
        Locked,
        Claim,       // ended & can claim
        StartNext,   // ended & ready to start next round
        InRace,
        Entry,
        Sleeping     // next in / nothing to do
    }

    public sealed class HudContextToken
    {
        public RaceHudContext Ctx { get; private set; }
        public void Set(in RaceHudContext ctx) => Ctx = ctx;
    }
}
