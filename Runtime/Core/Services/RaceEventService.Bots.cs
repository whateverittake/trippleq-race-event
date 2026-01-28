using System;
using System.Collections.Generic;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed partial class RaceEventService
    {
        internal void SeedBots(RaceRun run, long utcNow)
        {
            // MVP pool (localization làm sau)
            var pool = _botPool.Bots;
            if (pool == null || pool.Count == 0)
            {
                Log("BotPool is empty. Please export race_bot_pool.json");
                return;
            }

            run.Opponents.Clear();
            // Pick first N-1 (later: random w/ RNG provider)
            var need = System.Math.Max(0, ActiveConfigForRunOrCursor().PlayersPerRace - 1);

            // 1) Tính quota theo config
            var comp = ActiveConfigForRunOrCursor().BotComposition;
            // Nếu tổng quota != need => scale / clamp cho đúng need
            NormalizeComposition(ref comp, need);

            var levelFilteredPool = FilterByPlayerLevelOrClosest(pool, CurrentLevel);

            if (levelFilteredPool.Count == 0)
            {
                // fallback: tuyệt đối không được thiếu
                levelFilteredPool = new List<BotProfile>(pool);
            }

            // 1) Add theo quota (ưu tiên đúng level trước)
            AddFromPool(run, levelFilteredPool, BotPersonality.Boss, comp.BossCount, utcNow, allowDuplicate: false);
            AddFromPool(run, levelFilteredPool, BotPersonality.Normal, comp.NormalCount, utcNow, allowDuplicate: false);
            AddFromPool(run, levelFilteredPool, BotPersonality.Noob, comp.NoobCount, utcNow, allowDuplicate: false);

            // 2) Nếu thiếu theo từng type -> bù đúng type từ FULL POOL (bất chấp level)
            EnsurePersonalityCount(run, levelFilteredPool, pool, BotPersonality.Boss, comp.BossCount, utcNow);
            EnsurePersonalityCount(run, levelFilteredPool, pool, BotPersonality.Normal, comp.NormalCount, utcNow);
            EnsurePersonalityCount(run, levelFilteredPool, pool, BotPersonality.Noob, comp.NoobCount, utcNow);

            // 3) Nếu vẫn thiếu tổng -> fill bằng bot gần level yêu cầu nhất (bất kỳ type, allowDuplicate)
            int remaining = need - run.Opponents.Count;
            if (remaining > 0)
                FillRemainingClosestLevel(run, pool, remaining, utcNow, CurrentLevel);

            // Tới đây mà vẫn thiếu thì chỉ có thể là pool rỗng
            if (run.Opponents.Count < need)
            {
                Log($"[ERROR] SeedBots cannot fill need={need}, got={run.Opponents.Count}. BotPool empty?");
            }

            // 4) Shuffle để boss không luôn đứng đầu
            RaceEventUtil.Shuffle(run.Opponents);
        }

        private static void NormalizeComposition(ref RaceBotComposition comp, int need)
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

        private int AddFromPool(
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
            need -= AddFromPool(run, levelFilteredPool, type, need, utcNow, allowDuplicate: false);

            // Nếu vẫn thiếu: lấy từ full pool (bất chấp level)
            if (need > 0)
                need -= AddFromPool(run, fullPool, type, need, utcNow, allowDuplicate: true);
        }

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

        private int AddAnyFromPool(
                            RaceRun run,
                            IReadOnlyList<BotProfile> pool,
                            int count,
                            long utcNow,
                            bool allowDuplicate = true)
        {
            if (count <= 0) return 0;
            if (pool == null || pool.Count == 0) return 0;

            int added = 0;

            for (int k = 0; k < count; k++)
            {
                var pick = pool[UnityEngine.Random.Range(0, pool.Count)];

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

        private static List<BotProfile> FilterByPlayerLevel(
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

        private static List<BotProfile> FilterByPlayerLevelOrClosest(
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

        private static int DistanceToRange(int level, int min, int max)
        {
            if (level < min) return min - level;
            if (level > max) return level - max;
            return 0; // inside range
        }

    }
}
