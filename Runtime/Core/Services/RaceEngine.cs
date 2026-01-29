using System;
using System.Collections.Generic;

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
            var need = System.Math.Max(0, cfg.PlayersPerRace - 1);

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

            // 4) Shuffle để boss không luôn đứng đầu
            Shuffle(run.Opponents);
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
            unchecked
            {
                int h = 17;
                h = h * 31 + run.Player.LevelsCompleted;
                for (int i = 0; i < run.Opponents.Count; i++)
                    h = h * 31 + run.Opponents[i].LevelsCompleted;
                return h;
            }
        }

        //Compute standings bằng RaceStandings.Compute(...) theo 1 rule duy nhất.
        //Trả về top N + rank của player + playerFinished.
        //Service chỉ gọi để render UI / popup.
        public LeaderboardSnapshot BuildLeaderboardSnapshot(RaceRun run, int topN)
        {
            if (run == null) return LeaderboardSnapshot.Empty();

            // 1) lấy standings theo cùng 1 rule duy nhất
            // nếu RaceStandings.Compute đã sort đúng theo rule finishUtc / progress thì dùng lại luôn
            var standings = RaceStandings.Compute(run.AllParticipants(), run.GoalLevels);

            // 2) lấy rank player
            int playerRank = standings.FindIndex(p => p.Id == run.Player.Id) + 1;
            if (playerRank <= 0) playerRank = standings.Count;

            // 3) topN
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
