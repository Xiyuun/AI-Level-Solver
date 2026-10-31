namespace LevelForge.Platform.Tests;

// 接入套件 skill 九件套与 harness 模板的齐备性守护（打包遗漏防线）
// 逐文件核对存在性与内容关键点（对照《接入套件skill详设》各节）
public sealed class SkillsKitTests
{
    // 从测试程序集位置向上找仓库根（含 LevelForge.slnx 的目录）
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LevelForge.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("未找到仓库根");
    }

    private static string SkillPath(string relative)
    {
        return Path.Combine(RepoRoot(), "skills", "onboarding-skill", relative);
    }

    private static string ReadSkill(string relative)
    {
        var path = SkillPath(relative);
        Assert.True(File.Exists(path), $"skill 文件缺失：{relative}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void 九件套齐备_含目录件()
    {
        var files = new[]
        {
            "README.md", "qualification.md", "onboarding.md", "contract.md",
            "replay-spec.md", "checklist.md", "deliverables.md",
            Path.Combine("test-template", "README.md"),
            Path.Combine("reference", "README.md"),
        };
        foreach (var file in files)
        {
            Assert.True(File.Exists(SkillPath(file)), $"九件套缺件：{file}");
        }
    }

    [Fact]
    public void README_行为守则与两阶段运行前置()
    {
        var readme = ReadSkill("README.md");
        Assert.Contains("两阶段运行", readme);          // 先资格后生成
        Assert.Contains("必须问人，禁止猜", readme);      // 守则1
        Assert.Contains("REVIEW", readme);               // 标注义务
        Assert.Contains("MANIFEST", readme);             // 清单义务
        Assert.Contains("零改动", readme);               // 发布逻辑零改动
        Assert.Contains("版本", readme);                 // skill 版本号义务
    }

    [Fact]
    public void qualification_四问与证据义务()
    {
        var qualification = ReadSkill("qualification.md");
        Assert.Contains("回合制", qualification);
        Assert.Contains("确定性", qualification);
        Assert.Contains("状态可枚举", qualification);
        Assert.Contains("持续产关", qualification);
        Assert.Contains("证据", qualification);           // 无证据结论视为未完成
        Assert.Contains("卫生度", qualification);         // 附加产出
    }

    [Fact]
    public void contract_七成员语义合同与铁律()
    {
        var contract = ReadSkill("contract.md");
        foreach (var member in new[]
                 {
                     "LoadLevel", "GetLegalMoves", "ApplyMove", "IsGoal",
                     "StateKey", "DescribeMove", "Snapshot",
                 })
        {
            Assert.Contains(member, contract);
        }
        Assert.Contains("禁止静默修复", contract);
        Assert.Contains("逐字一致", contract);              // IsGoal 与本体判定
        Assert.Contains("零引擎依赖", contract);             // 三铁律
        Assert.Contains("moveSchemaVer", contract);        // 执行协议
        Assert.Contains("禁止共用适配器 ApplyMove", contract); // 独立性
    }

    [Fact]
    public void onboarding_七步工作流带验收标准()
    {
        var onboarding = ReadSkill("onboarding.md");
        Assert.Contains("定位规则代码", onboarding);
        Assert.Contains("rule-matrix", onboarding);
        Assert.Contains("视觉映射", onboarding);
        Assert.Contains("玩法规则描述", onboarding);
        Assert.Contains("对拍驱动", onboarding);
        Assert.Contains("验收标准", onboarding);
    }

    [Fact]
    public void checklist_四大组13条与歧义禁勾()
    {
        var checklist = ReadSkill("checklist.md");
        Assert.Contains("A. 规则语义", checklist);
        Assert.Contains("B. 契约", checklist);
        Assert.Contains("C. 工程", checklist);
        Assert.Contains("D. harness", checklist);
        // 13 条逐条在场（编号-勾选框成对）
        for (var i = 1; i <= 13; i++)
        {
            Assert.Contains($"- [ ] {i}.", checklist);
        }
        Assert.Contains("禁止直接勾选", checklist);
        Assert.Contains("rule-matrix", checklist);         // 配套必交
    }

    [Fact]
    public void replay_spec_执行契约与假对拍判定()
    {
        var replay = ReadSkill("replay-spec.md");
        Assert.Contains("假对拍", replay);
        Assert.Contains("生产加载路径", replay);
        Assert.Contains("真实输入入口", replay);
        Assert.Contains("result.json", replay);
        Assert.Contains("不能只回显请求值", replay);        // levelHash 必须实算
        Assert.Contains("人工对拍降级", replay);
    }

    [Fact]
    public void deliverables_清单与MANIFEST格式()
    {
        var deliverables = ReadSkill("deliverables.md");
        Assert.Contains("MANIFEST.json", deliverables);
        Assert.Contains("onboarding-output", deliverables);
        Assert.Contains("mechanics-table.json", deliverables);
        Assert.Contains("visual-mapping.json", deliverables);
        Assert.Contains("不能只回显请求中的哈希", deliverables);
    }

    [Fact]
    public void test_template_通用骨架与维度判别生成器()
    {
        var template = ReadSkill(Path.Combine("test-template", "README.md"));
        Assert.Contains("LoadLevel", template);
        Assert.Contains("纯函数", template);
        Assert.Contains("维度判别", template);
        Assert.Contains("不能只比较 StateKey", template);
    }

    [Fact]
    public void reference_指向玩具实现并明示非复制模板()
    {
        var reference = ReadSkill(Path.Combine("reference", "README.md"));
        Assert.Contains("SokobanToy", reference);
        Assert.Contains("不是复制模板", reference);
        Assert.Contains("门2不适用", reference);            // 玩具诚实边界
    }

    [Fact]
    public void harness模板_批处理骨架与人工对拍清单()
    {
        var unity = Path.Combine(RepoRoot(), "harness-templates", "unity-batchmode", "PairingHarness.cs");
        Assert.True(File.Exists(unity), "Unity batchmode 模板缺失");
        var code = File.ReadAllText(unity);
        Assert.Contains("--level", code);                  // 启动契约参数
        Assert.Contains("--tape", code);
        Assert.Contains("--out", code);
        Assert.Contains("生产加载路径", code);               // REVIEW 审核点
        Assert.Contains("真实输入入口", code);
        Assert.Contains("WaitForSettlement", code);        // 结算等待（禁固定 sleep）

        var manual = Path.Combine(RepoRoot(), "harness-templates", "manual-pairing-checklist.md");
        Assert.True(File.Exists(manual), "人工对拍清单模板缺失");
        var text = File.ReadAllText(manual);
        Assert.Contains("10 关", text);                    // 20 选 10
        Assert.Contains("签认", text);
        Assert.Contains("仅最终结果", text);                // 证据强度注明
    }
}
