namespace LevelForge.Adapters.PiggyFarm;

// 生成质量门（仅作用于生成候选，不拦导入的真实关卡）：
// 格式合法 ≠ 设计可用——拦截"全部同朝向/独立直道/初始全可走"的规则网格。
// 静态助手：facade 对外暴露 API，composer 在布点环内提前自查（不合格不烧候选预算）。
internal static class PiggyFarmQuality
{
    public static IReadOnlyList<string> Errors(string levelJson)
    {
        var format = PiggyFarmLevels.Validate(levelJson);
        if (format.Count > 0) return format;   // 格式问题交给 FormatErrors 处理，这里不重复报
        var level = PiggyFarmLevels.Load(levelJson);
        var pigs = level.Pigs;
        var errors = new List<string>();

        var facings = pigs.Select(p => p.Facing).Distinct().Count();
        if (pigs.Count >= 8 && facings < 3)
        {
            errors.Add($"朝向过少：{pigs.Count} 只猪只有 {facings} 种朝向（至少 3 种；禁止全部同向/单排直道）");
        }

        var adapter = new PiggyFarmAdapter();
        var state = adapter.LoadLevel(level);
        // "可直接起步" = 能沿朝向滑行（不含原地顶撞——顶撞是 v1.1 起的合法动作，但不代表该猪未被阻挡）
        // 小黄鸭不可点击，也不计入起步
        var legal = state.Pigs.Count(p => p.Type != "Duck" && PiggyFarmAdapter.CanAdvance(state, p));
        if (pigs.Count >= 8 && legal * 2 > pigs.Count)
        {
            errors.Add($"初始可走猪过多：{legal}/{pigs.Count} 只可直接起步（需 ≤50%，要有阻挡依赖）");
        }
        var blocked = pigs.Count - legal;
        var requiredBlocked = Math.Max(2, pigs.Count / 5);
        if (pigs.Count >= 8 && blocked < requiredBlocked)
        {
            errors.Add($"受阻猪过少：仅 {blocked}/{pigs.Count} 只被其它猪阻挡（至少 {requiredBlocked} 只）");
        }

        return errors;
    }
}
