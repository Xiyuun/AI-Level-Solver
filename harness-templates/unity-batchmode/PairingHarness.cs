// PairingHarness.cs · Unity batchmode 对拍 harness 模板骨架（replay-spec.md §4 的落地起点）
// 放置：Assets/Editor/LevelForgeHarness/PairingHarness.cs（发布逻辑零改动；只加 Editor 脚本）
// 启动：Unity.exe -batchmode -nographics -projectPath <项目> -executeMethod PairingHarness.Run --level <文件> --tape <文件> --out <文件>
//
// 审核要点（checklist D 组 12/13 条对应）：
//   加载 → 走生产加载路径；注入 → 走真实输入入口；状态/胜负 → 从游戏本体读取；结算 → 等业务完成信号
//   每个调用点的 REVIEW 标注是给接入 agent 的填空位，删掉标注前必须能回答"这里确实走了真实路径吗"

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

public static class PairingHarness
{
    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        string levelPath = null, tapePath = null, outPath = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--level") levelPath = args[i + 1];
            if (args[i] == "--tape") tapePath = args[i + 1];
            if (args[i] == "--out") outPath = args[i + 1];
        }
        if (levelPath is null || tapePath is null || outPath is null)
        {
            EditorApplication.Exit(2);   // 参数缺失 = harness 自身失败（非零退出码）
        }
        try
        {
            var report = Execute(levelPath!, tapePath!);
            File.WriteAllText(outPath!, JsonSerializer.Serialize(report, JsonOpts));
            EditorApplication.Exit(0);   // 0 = 正常执行并有结果（与胜负无关）
        }
        catch (Exception ex)
        {
            File.WriteAllText(outPath!, JsonSerializer.Serialize(new
            {
                outcome = "error",
                error = ex.Message,
            }, JsonOpts));
            EditorApplication.Exit(1);
        }
    }

    private static object Execute(string levelPath, string tapePath)
    {
        // ---- 1. 加载：走生产加载路径（REVIEW: 确认这里调用的是玩家进关的同一代码路径） ----
        var levelBytes = File.ReadAllBytes(levelPath);
        // GameManager.LoadLevelForPlay(Encoding.UTF8.GetString(levelBytes));   // ← 项目真实加载入口
        throw new NotImplementedException("REVIEW: 接入时替换为项目的生产加载调用");

        // ---- 2. 初始语义状态：从游戏本体读取（REVIEW: 不得调用适配器投影来伪造实报） ----
        // var initial = ExtractSemanticState();   // ← 项目语义字段：位置/朝向/库存/剩余资源…

        // ---- 3. 逐步注入：走真实输入入口（REVIEW: 与玩家点击同一处理函数，禁止直接改内部状态） ----
        // foreach (var step in tape.steps)
        // {
        //     InputRouter.Handle(step.move);            // ← 项目真实输入入口
        //     WaitForSettlement();                      // ← 等业务结算完成信号（帧推进/事件/标志位），禁止固定 sleep 冒充
        //     记录 accepted/reason + semanticState + semanticStateHash;
        // }

        // ---- 4. 胜负：读游戏自身判定（REVIEW: GameManager 的胜利事件/状态，不做画面识别） ----
        // var outcome = GameManager.Instance.IsWin ? "win" : …;
        // return new { levelHash = Sha256(levelBytes), initial, steps, outcome, finalSemanticState, … };
    }

    // ---- 可复用工具（按项目实况改） ----

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // 等待业务结算：batchmode 无主循环 tick，需主动推帧（replay-spec.md §4.5 坑清单）
    private static void WaitForSettlement()
    {
        // REVIEW: 用项目的结算完成信号（事件/标志位）；以下仅是推帧示例
        // for (var i = 0; i < MaxFrames && !Settled(); i++) EditorApplication.Step();
        throw new NotImplementedException("REVIEW: 替换为项目的结算完成判定");
    }

    private static string Sha256(byte[] bytes)
    {
        return "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
