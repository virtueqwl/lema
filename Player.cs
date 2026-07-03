using static GameInputTester.WinApi;

namespace GameInputTester;

/// <summary>冷却驱动的加权洗牌回放器。每轮保证所有键都被触发一次（顺序随机），跨轮 lastTrigger 保留。</summary>
public class CooldownPlayer
{
    /// <summary>
    /// CooldownMs            基础冷却（毫秒）
    /// JitterMs              0~+N 抖动（毫秒，只延后不提前）
    /// AfterTriggerWaitMinMs 该键发完后到选下一个键的最小等待（毫秒）
    /// AfterTriggerWaitMaxMs 该键发完后到选下一个键的最大等待（毫秒），< min 时退化为 min
    /// Weight                加权（整数，0=禁用）
    /// </summary>
    public record Slot(
        string Logical, Keys Physical,
        int CooldownMs, int JitterMs,
        int AfterTriggerWaitMinMs, int AfterTriggerWaitMaxMs,
        int Weight);

    public List<Slot> Slots { get; set; } = new();
    /// <summary>执行轮数。0 = 无限循环。</summary>
    public int Rounds { get; set; } = 1;
    /// <summary>
    /// true  = 每轮开始时重置 lastTrigger（每轮独立测试，所有键从就绪状态出发）
    /// false = 跨轮累计 lastTrigger（默认，长时间压测，CD 真的"过完"才发）
    /// </summary>
    public bool ResetLastTriggerPerRound { get; set; } = false;

    public async Task RunAsync(CancellationToken ct, IProgress<string>? progress = null)
    {
        var rng = new Random();

        // lastTrigger 初值用 0L（远早于 Environment.TickCount64），首轮所有键就绪
        var lastTrigger = Slots.ToDictionary(s => s.Physical, _ => 0L);

        for (int round = 1; Rounds == 0 || round <= Rounds; round++)
        {
            // 重置模式：每轮开始时清空 lastTrigger，让所有键从就绪状态出发
            if (ResetLastTriggerPerRound)
                lastTrigger = Slots.ToDictionary(s => s.Physical, _ => 0L);

            // 加权展开 + 洗牌
            var pool = BuildWeightedPool(Slots, rng);

            while (pool.Count > 0)
            {
                if (ct.IsCancellationRequested) return;

                var pick = pool[rng.Next(pool.Count)];
                pool.Remove(pick);

                // 1. 等到该键冷却好
                int jitter = pick.JitterMs > 0 ? rng.Next(0, pick.JitterMs + 1) : 0;
                int cd = pick.CooldownMs + jitter;
                var readyAt = lastTrigger[pick.Physical] + cd;
                var waitForCd = readyAt - Environment.TickCount64;
                if (waitForCd > 0) await Task.Delay((int)waitForCd, ct);
                if (ct.IsCancellationRequested) return;

                // 2. 发键
                SendScanCode(ScanCodeOf(pick.Physical));
                lastTrigger[pick.Physical] = Environment.TickCount64;

                progress?.Report(
                    $"轮 {round} | {pick.Logical} ({pick.Physical}) | cd={cd}ms");

                // 3. 发完后等"操作间隔"（除非是本轮最后一个）
                if (pool.Count > 0)
                {
                    int lo = Math.Max(0, Math.Min(pick.AfterTriggerWaitMinMs, pick.AfterTriggerWaitMaxMs));
                    int hi = Math.Max(0, Math.Max(pick.AfterTriggerWaitMinMs, pick.AfterTriggerWaitMaxMs));
                    int afterWait = (hi <= 0) ? 0
                                  : (lo == hi) ? lo
                                  : rng.Next(lo, hi);
                    if (afterWait > 0) await Task.Delay(afterWait, ct);
                }
            }
        }
    }

    private static List<Slot> BuildWeightedPool(List<Slot> src, Random rng)
    {
        var pool = new List<Slot>();
        foreach (var s in src)
        {
            int w = Math.Max(0, s.Weight);
            for (int i = 0; i < w; i++) pool.Add(s);
        }
        // Fisher-Yates 洗牌，避免同键聚集
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool;
    }
}

/// <summary>旧版"录制脚本"回放器，按数组顺序依次发键 + 区间随机延迟。保留以兼容已录制的脚本。</summary>
public class ScriptPlayer
{
    public record Step(string Logical, Keys Physical, int WaitMinMs, int WaitMaxMs);

    public List<Step> Steps { get; set; } = new();
    public bool Loop { get; set; } = false;
    public int InterKeyGapMs { get; set; } = 50;

    public async Task RunAsync(CancellationToken ct, IProgress<string>? progress = null)
    {
        var rng = new Random();
        do
        {
            foreach (var s in Steps)
            {
                if (ct.IsCancellationRequested) return;
                SendScanCode(ScanCodeOf(s.Physical));
                await Task.Delay(InterKeyGapMs, ct);
                progress?.Report($"script | {s.Logical} ({s.Physical})");

                int lo = Math.Min(s.WaitMinMs, s.WaitMaxMs);
                int hi = Math.Max(s.WaitMinMs, s.WaitMaxMs);
                if (hi > 0)
                {
                    int wait = lo == hi ? lo : rng.Next(lo, hi);
                    await Task.Delay(wait, ct);
                }
            }
        } while (Loop);
    }
}
