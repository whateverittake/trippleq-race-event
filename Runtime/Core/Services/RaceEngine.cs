using System;
using System.Collections.Generic;
using System.Linq;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceEngine
    {
        //Đảm bảo run.Opponents được fill đủ PlayersPerRace - 1.
        //Lọc bot theo level(hoặc closest), chia theo tỷ lệ Noob/Normal/Boss theo config.
        //Nếu thiếu thì fallback sang full pool và cho phép duplicate để không bao giờ thiếu bot.
        //Shuffle để boss không luôn đứng đầu.
        public void EnsureBotsSeeded(RaceRun run, long utcNow, int currentLevel, RaceEventConfig cfg, BotPoolJson botPool, out string logString)
        {
            logString= string.Empty;
            // MVP pool (localization làm sau)
            var pool = botPool.Bots;
            if (pool == null || pool.Count == 0)
            {
                logString = "BotPool is empty. Please export race_bot_pool.json";
                return;
            }

            run.Opponents.Clear();
            // Pick first N-1 (later: random w/ RNG provider)
            int roundIndex = run != null ? run.RoundIndex : 0; // no-run => round0
            var need = System.Math.Max(0, cfg.GetRoundSettings(roundIndex).PlayersPerRace - 1);

            // 1) Tính quota theo config
            var comp = cfg.BotComposition;
            // Nếu tổng quota != need => scale / clamp cho đúng need
            NormalizeComposition(ref comp, need);

            var levelFilteredPool = FilterByPlayerLevelOrClosest(pool, currentLevel);

            if (levelFilteredPool.Count == 0)
            {
                // fallback: tuyệt đối không được thiếu
                levelFilteredPool = new List<BotProfile>(pool);
            }

            // 1) Add theo quota (ưu tiên đúng level trước)
            AddBotsFromPool(run, levelFilteredPool, BotPersonality.Boss, comp.BossCount, utcNow, allowDuplicate: false);
            AddBotsFromPool(run, levelFilteredPool, BotPersonality.Normal, comp.NormalCount, utcNow, allowDuplicate: false);
            AddBotsFromPool(run, levelFilteredPool, BotPersonality.Noob, comp.NoobCount, utcNow, allowDuplicate: false);

            // 2) Nếu thiếu theo từng type -> bù đúng type từ FULL POOL (bất chấp level)
            EnsurePersonalityCount(run, levelFilteredPool, pool, BotPersonality.Boss, comp.BossCount, utcNow);
            EnsurePersonalityCount(run, levelFilteredPool, pool, BotPersonality.Normal, comp.NormalCount, utcNow);
            EnsurePersonalityCount(run, levelFilteredPool, pool, BotPersonality.Noob, comp.NoobCount, utcNow);

            // 3) Nếu vẫn thiếu tổng -> fill bằng bot gần level yêu cầu nhất (bất kỳ type, allowDuplicate)
            int remaining = need - run.Opponents.Count;
            if (remaining > 0)
                FillRemainingClosestLevel(run, pool, remaining, utcNow, currentLevel);

            // Tới đây mà vẫn thiếu thì chỉ có thể là pool rỗng
            if (run.Opponents.Count < need)
            {
                logString = $"[ERROR] SeedBots cannot fill need={need}, got={run.Opponents.Count}. BotPool empty?";
            }

            // 3.5) Tune bot params theo nhịp race hiện tại.
            // BotPool (json) nên chỉ là "identity/profile"; tốc độ + humanization phải scale theo run.
            TuneBotsForRun(run, utcNow);

            // 4) Shuffle để boss không luôn đứng đầu
            Shuffle(run.Opponents);
        }

        // Scale bot speed/humanization theo run để phù hợp race ngắn (8h/race nhưng gameplay thực 10-20 phút).
        // Mục tiêu:
        // - AvgSecondsPerLevel bám sát nhịp gameplay (≈ 120s/level) thay vì phụ thuộc data 24h.
        // - Tắt/giảm Sleep + Stuck cho race ngắn để leaderboard không "đứng hình".
        private void TuneBotsForRun(RaceRun run, long utcNow)
        {
            if (run == null) return;

            // Heuristic race ngắn: goal nhỏ hoặc tổng thời gian dự kiến (goal * 120s) <= 30 phút.
            bool isShortRace = run.GoalLevels > 0 &&
                               (run.GoalLevels <= 12 || (run.GoalLevels * 120) <= 1800);

            // Base pace: assume 2 phút / level (design mới). Clamp để không cực đoan.
            float baseSecondsPerLevel = 120f;
            if (run.GoalLevels > 0)
            {
                // dự kiến tổng thời gian bot finish: 10-25 phút (tuỳ goal)
                float targetFinishSeconds = UnityEngine.Mathf.Clamp(
                    run.GoalLevels * 120f,
                    600f,
                    1500f
                );

                baseSecondsPerLevel = UnityEngine.Mathf.Clamp(
                    targetFinishSeconds / run.GoalLevels,
                    70f,
                    180f
                );
            }

            for (int i = 0; i < run.Opponents.Count; i++)
            {
                var bot = run.Opponents[i];
                if (bot == null || !bot.IsBot) continue;

                // Pace multiplier theo personality.
                float mult = GetPaceMultiplier(bot.Personality);
                bot.AvgSecondsPerLevel = baseSecondsPerLevel * mult;

                if (isShortRace)
                {
                    // Race ngắn: disable sleep/stuck để tránh bot đứng yên quá lâu.
                    bot.SleepDurationHours = 0;
                    bot.StuckChancePerHour = 0f;
                    bot.StuckMinMinutes = 0;
                    bot.StuckMaxMinutes = 0;
                    bot.StuckUntilUtcSeconds = 0;

                    // Jitter nhẹ hơn để standings ổn định (vẫn có cảm giác "người").
                    bot.JitterPct = UnityEngine.Mathf.Clamp(bot.JitterPct, 0.05f, 0.12f);
                }
                else
                {
                    // Race dài (legacy): giữ nguyên data trong bot pool.
                    // Tuy nhiên tránh trường hợp stuckUntil nằm quá xa tương lai khi restart.
                    if (bot.StuckUntilUtcSeconds < utcNow)
                        bot.StuckUntilUtcSeconds = 0;
                }
            }
        }

        private float GetPaceMultiplier(BotPersonality p)
        {
            // Gợi ý phân phối:
            // - Boss nhanh hơn
            // - Noob chậm hơn
            // - Normal bám base
            switch (p)
            {
                case BotPersonality.Boss:
                    return UnityEngine.Random.Range(0.75f, 0.92f);
                case BotPersonality.Noob:
                    return UnityEngine.Random.Range(1.20f, 1.55f);
                default:
                    return UnityEngine.Random.Range(0.95f, 1.20f);
            }
        }


        //Chuẩn hoá quota bot(Boss/Normal/Noob) sao cho tổng đúng bằng need.
        //Nếu thiếu → dồn vào Noob.
        //Nếu dư → trừ Noob trước, rồi Normal, rồi Boss.
        private void NormalizeComposition(ref RaceBotComposition comp, int need)
        {
            // nếu GD set tổng khác need: ưu tiên giữ Boss/Normal, phần còn lại đổ vào Noob
            var sum = comp.Total;
            if (sum == need) return;

            if (sum < need)
            {
                comp.NoobCount += (need - sum);
                return;
            }

            // sum > need => giảm Noob trước, rồi Normal, cuối cùng Boss
            int extra = sum - need;

            int dec = Math.Min(comp.NoobCount, extra);
            comp.NoobCount -= dec; extra -= dec;

            dec = Math.Min(comp.NormalCount, extra);
            comp.NormalCount -= dec; extra -= dec;

            dec = Math.Min(comp.BossCount, extra);
            comp.BossCount -= dec; extra -= dec;
        }

        //Đảm bảo đủ số lượng bot của 1 personality(Boss/Normal/Noob).
        //Ưu tiên lấy từ pool đã filter theo level, thiếu nữa mới lấy từ full pool.
        //Mục tiêu là đúng quota theo config.
        private void EnsurePersonalityCount(
                                            RaceRun run,
                                            IReadOnlyList<BotProfile> levelFilteredPool,
                                            IReadOnlyList<BotProfile> fullPool,
                                            BotPersonality type,
                                            int targetCount,
                                            long utcNow)
        {
            if (targetCount <= 0) return;

            int current = 0;
            for (int i = 0; i < run.Opponents.Count; i++)
                if (run.Opponents[i].IsBot && run.Opponents[i].Id.StartsWith(type.ToString().ToLowerInvariant()))
                    current++;

            int need = targetCount - current;
            if (need <= 0) return;

            // Ưu tiên: trong pool đã filter theo level trước
            need -= AddBotsFromPool(run, levelFilteredPool, type, need, utcNow, allowDuplicate: false);

            // Nếu vẫn thiếu: lấy từ full pool (bất chấp level)
            if (need > 0)
                need -= AddBotsFromPool(run, fullPool, type, need, utcNow, allowDuplicate: true);
        }

        private int AddBotsFromPool(
            RaceRun run,
            IReadOnlyList<BotProfile> pool,
            BotPersonality type,
            int count,
            long utcNow,
            bool allowDuplicate = false)
        {
            if (count <= 0) return 0;
            if (pool == null || pool.Count == 0) return 0;

            // lọc candidates theo personality
            var candidates = new List<BotProfile>();
            for (int i = 0; i < pool.Count; i++)
                if (pool[i].Personality == type) candidates.Add(pool[i]);

            if (candidates.Count == 0) return 0;

            int added = 0;

            // pick random
            for (int k = 0; k < count; k++)
            {
                var pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];

                if (!allowDuplicate)
                {
                    bool existed = false;
                    for (int j = 0; j < run.Opponents.Count; j++)
                        if (run.Opponents[j].Id == pick.Id) { existed = true; break; }

                    if (existed) { k--; continue; }
                }

                run.Opponents.Add(pick.ToRaceParticipant(utcNow));
                added++;
            }

            return added;
        }

        //Nếu sau khi fill theo quota mà vẫn thiếu tổng số bot → fill thêm bằng nhóm bot “closest level”.
        //Cho phép duplicate để guarantee đủ.
        private void FillRemainingClosestLevel(
                                                RaceRun run,
                                                IReadOnlyList<BotProfile> fullPool,
                                                int remaining,
                                                long utcNow,
                                                int playerLevel)
        {
            if (remaining <= 0) return;
            if (fullPool == null || fullPool.Count == 0) return;

            // lấy cụm closest (hàm sẵn có)
            var closestPool = FilterByPlayerLevelOrClosest(fullPool, playerLevel);
            if (closestPool == null || closestPool.Count == 0) closestPool = new List<BotProfile>(fullPool);

            // allowDuplicate true để guarantee đủ
            for (int i = 0; i < remaining; i++)
            {
                var pick = closestPool[UnityEngine.Random.Range(0, closestPool.Count)];
                run.Opponents.Add(pick.ToRaceParticipant(utcNow));
            }
        }

        //Lọc bot có khoảng level chứa playerLevel (min/max).
        private List<BotProfile> FilterByPlayerLevel(
                                            IReadOnlyList<BotProfile> pool,
                                            int playerLevel)
        {
            var list = new List<BotProfile>();
            for (int i = 0; i < pool.Count; i++)
            {
                var b = pool[i];
                if (playerLevel >= b.MinPlayerLevel &&
                    playerLevel <= b.MaxPlayerLevel)
                {
                    list.Add(b);
                }
            }
            return list;
        }

        //Nếu có bot match range chính xác → dùng.
        //Nếu không → tìm nhóm bot có khoảng level “gần nhất” với player.
        private List<BotProfile> FilterByPlayerLevelOrClosest(
                                           IReadOnlyList<BotProfile> pool,
                                           int playerLevel)
        {
            // 1) try exact match
            var exact = new List<BotProfile>();
            for (int i = 0; i < pool.Count; i++)
            {
                var b = pool[i];
                if (playerLevel >= b.MinPlayerLevel && playerLevel <= b.MaxPlayerLevel)
                    exact.Add(b);
            }
            if (exact.Count > 0) return exact;

            // 2) fallback: closest range
            int best = int.MaxValue;
            for (int i = 0; i < pool.Count; i++)
            {
                var b = pool[i];
                int d = DistanceToRange(playerLevel, b.MinPlayerLevel, b.MaxPlayerLevel);
                if (d < best) best = d;
            }

            // collect all bots with best distance (giữ đa dạng)
            var closest = new List<BotProfile>();
            for (int i = 0; i < pool.Count; i++)
            {
                var b = pool[i];
                int d = DistanceToRange(playerLevel, b.MinPlayerLevel, b.MaxPlayerLevel);
                if (d == best) closest.Add(b);
            }

            return closest;
        }

        //Shuffle danh sách bot để tránh bias (boss luôn đầu list).
        private void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        //Tính khoảng cách từ level tới một khoảng [min,max] để tìm “closest”.
        private int DistanceToRange(int level, int min, int max)
        {
            if (level < min) return min - level;
            if (level > max) return level - max;
            return 0; // inside range
        }

        //Hash nhanh dựa trên levelsCompleted (player + bots) để phát hiện thay đổi.
        //Dùng để tránh spam Save/Publish mỗi tick khi không đổi.
        public int ComputeRunProgressHash(RaceRun run)
        {
            if (run == null) return 0;

            unchecked
            {
                int h = 17;

                // player
                h = h * 31 + run.Player.LevelsCompleted;
                h = h * 31 + (run.Player.HasFinished ? 1 : 0);

                // opponents / bots
                var opps = run.Opponents; // hoặc Bots
                for (int i = 0; i < opps.Count; i++)
                {
                    var p = opps[i];
                    h = h * 31 + p.LevelsCompleted;
                    h = h * 31 + (p.HasFinished ? 1 : 0);       
                }

                return h;
            }
        }

        //Compute standings bằng RaceStandings.Compute(...) theo 1 rule duy nhất.
        //Trả về top N + rank của player + playerFinished.
        //Service chỉ gọi để render UI / popup.
        public LeaderboardSnapshot BuildLeaderboardSnapshot(RaceRun run, int topN)
        {
            if (run == null) return LeaderboardSnapshot.Empty();

            // Build list: player first, then opponents (để stable order)
            var all = new List<RaceParticipant>(1 + run.Opponents.Count) { run.Player };
            all.AddRange(run.Opponents);

            var orderIndex = new Dictionary<RaceParticipant, int>(all.Count);
            for (int i = 0; i < all.Count; i++) orderIndex[all[i]] = i;

            var player = run.Player;

            // ✅ Standings rule:
            // - finished first (by finish time, fallback)
            // - unfinished by levels desc
            // - unfinished tie (same level): player ALWAYS loses tie (stays after bots)
            var standings = all
                .OrderBy(p => p.HasFinished ? 0 : 1)
                .ThenBy(p =>
                {
                    if (!p.HasFinished) return long.MaxValue;
                    if (p.FinishedUtcSeconds > 0) return p.FinishedUtcSeconds;
                    if (p.LastUpdateUtcSeconds > 0) return p.LastUpdateUtcSeconds;
                    return long.MaxValue;
                })
                .ThenByDescending(p => p.HasFinished ? int.MinValue : p.LevelsCompleted)
                .ThenBy(p =>
                {
                    if (p.HasFinished) return 0;
                    return ReferenceEquals(p, player) ? 1 : 0; // bot(0) trước, player(1) sau
                })
                .ThenBy(p => orderIndex[p])
                .ToList();

            // player rank
            int playerRank = standings.FindIndex(p => p.Id == run.Player.Id) + 1;
            if (playerRank <= 0) playerRank = standings.Count;

            // topN (popup thường dùng PlayersCount để đủ rank)
            var top = standings.Count <= topN ? standings : standings.GetRange(0, topN);

            return new LeaderboardSnapshot(top, playerRank, run.Player.HasFinished);
        }
    }

    public readonly struct LeaderboardSnapshot
    {
        public readonly IReadOnlyList<RaceParticipant> Top;
        public readonly int PlayerRank;      // 1-based
        public readonly bool PlayerFinished;

        public LeaderboardSnapshot(IReadOnlyList<RaceParticipant> top, int playerRank, bool playerFinished)
        {
            Top = top;
            PlayerRank = playerRank;
            PlayerFinished = playerFinished;
        }

        public static LeaderboardSnapshot Empty() => new LeaderboardSnapshot(Array.Empty<RaceParticipant>(), 0, false);
    }
}
