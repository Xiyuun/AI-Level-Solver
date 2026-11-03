// 文件职责：LevelForge 门2对拍 harness——batchmode 下把关卡 JSON 注入真实玩法、按磁带走真实点击、逐步上报语义状态。
// 对拍契约：--level <json> --tape <tape.json> --out <result.json>；退出码 0=正常执行有结果（与胜负无关），非 0=harness 自身失败。
// 真实性铁律（接入套件skill详设 §4.3）：加载走 GameManager 生产加载路径（EnterPlayTestMode+InitGameWithLevelDataAsync），
// 点击走 NormalPig.OnPointerClick（真实输入入口，与玩家点击同一处理函数），状态读游戏本体（SpawnedPigs），
// 结算等待 WaitForSettlement（IsBoardSettled 逐帧轮询，禁固定 sleep）——绕过任何一条即为假对拍。
// 语义 schema 与 SlidePigAdapter.ProjectSemanticState 对齐：{failed, pigs:[{id,type,facing,counter,cells:[{x,y}按x,y序]}按id序]}。
// 实战记录：2026-11-02 PiggyFarm 1 号关 batchmode 冒烟全绿（初始+逐步语义哈希一致、结局 win）——本文件即经真机裁决的参考实现。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using CyDp.Framework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LevelForgeHarness
{
    // 批处理入口（Unity: -batchmode -executeMethod LevelForgeHarness.PairingHarness.Run --level x --tape y --out z）
    public static class PairingHarness
    {
        // 关卡编辑器场景：内含 playRoot（GameManager/MapManager/PigManager）与 LevelPlayTestHost
        const string PlayScenePath = "Assets/Main/LevelEditor/Scene/LevelEditScene.unity";
        const string HarnessScriptPath = "Assets/Editor/LevelForgeHarness/PairingHarness.cs";

        // 各阶段超时（秒）：环境自举 / 关卡就绪 / 每步落定 / 整体
        const double BootstrapTimeoutSec = 90;
        const double LevelReadyTimeoutSec = 60;
        const double SettleTimeoutSec = 30;
        const double OverallTimeoutSec = 900;

        // 命令行参数（executeMethod 阶段解析后存 SessionState——静态字段在 PlayMode domain reload 时重置，SessionState 存活）
        const string SKeyPending = "LevelForgeHarness.Pending";
        const string SKeyLevel = "LevelForgeHarness.LevelPath";
        const string SKeyTape = "LevelForgeHarness.TapePath";
        const string SKeyOut = "LevelForgeHarness.OutPath";

        // 对拍磁带的最小读取模型（仅取 steps[].move.pigId 与 initial 期望）
        [Serializable]
        class TapeFile
        {
            public string levelRef;
            public int moveSchemaVer;
            public TapeStep[] steps;
            public string outcome;
        }

        [Serializable]
        class TapeStep
        {
            public int index;
            public TapeMove move;
        }

        [Serializable]
        class TapeMove
        {
            public string type;
            public int pigId;
        }

        // batchmode 入口：解析参数入 SessionState，打开玩法场景并进 PlayMode
        // 进入 Play 会触发 domain reload（静态字段与事件订阅全部重置），续跑由 InitializeOnLoadMethod 接管
        public static void Run()
        {
            try
            {
                if (!ParseArgs())
                {
                    EditorApplication.Exit(2);
                    return;
                }
                SessionState.SetBool(SKeyPending, true);
                EditorSceneManager.OpenScene(PlayScenePath, OpenSceneMode.Single);
                EditorApplication.isPlaying = true;   // 返回后编辑器继续循环，domain reload 后由 Resume 接管
            }
            catch (Exception ex)
            {
                File.WriteAllLines(SessionState.GetString(SKeyOut, "harness-error.log"), new[] { "harness bootstrap error: " + ex });
                EditorApplication.Exit(3);
            }
        }

        // 解析 --level/--tape/--out 并写入 SessionState
        static bool ParseArgs()
        {
            string level = null, tape = null, output = null;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--level") level = args[i + 1];
                else if (args[i] == "--tape") tape = args[i + 1];
                else if (args[i] == "--out") output = args[i + 1];
            }
            if (!File.Exists(level) || !File.Exists(tape) || string.IsNullOrEmpty(output))
                return false;
            SessionState.SetString(SKeyLevel, level);
            SessionState.SetString(SKeyTape, tape);
            SessionState.SetString(SKeyOut, output);
            return true;
        }

        // domain reload 后自动重新注册：编辑器脚本在每次加载时执行（含进入 Play 后的 reload）
        [InitializeOnLoadMethod]
        static void Resume()
        {
            if (!SessionState.GetBool(SKeyPending, false))
                return;   // 非对拍会话（正常打开编辑器）——不注册任何东西，零侵入
            if (Application.isPlaying)
                RunAsync().Forget();                       // 刚完成进入 Play 的 reload，直接续跑
            else
                EditorApplication.playModeStateChanged += OnPlayModeChanged;   // 等待进入 Play
        }

        // PlayMode 状态机：进入 Play 后启动对拍流程（domain reload 后由 Resume 重新注册）
        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode)
                return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            RunAsync().Forget();
        }

        // 对拍主流程（UniTask 挂在 PlayerLoop 上逐帧推进）
        static async UniTaskVoid RunAsync()
        {
            var levelPath = SessionState.GetString(SKeyLevel, "");
            var tapePath = SessionState.GetString(SKeyTape, "");
            var outPath = SessionState.GetString(SKeyOut, "");
            var report = new Report
            {
                levelRef = Path.GetFileNameWithoutExtension(levelPath),
                gameBuildFingerprint = "unity:" + Application.unityVersion + "/app:" + Application.version,
                harnessHash = HashFile(HarnessScriptPath),
                moveSchemaVer = 1,
                semanticSchemaVer = 1,
                executionEnvironment = "unity-editor-batchmode-playtest"
            };
            var exitCode = 0;
            var deadline = EditorApplication.timeSinceStartup + OverallTimeoutSec;
            try
            {
                // ① 关卡 JSON → LevelData（字段同名直读；terrain/special 缺省为空，v1 子集关不含这些）
                var levelJson = await File.ReadAllTextAsync(levelPath);
                var levelData = JsonUtility.FromJson<LevelData>(levelJson);
                if (levelData == null || levelData.pigs == null || levelData.pigs.Length == 0)
                    throw new InvalidOperationException("关卡 JSON 解析失败：无 pigs");

                // ② 激活玩法环境并等框架自举（与 LevelPlayTestHost.WaitForPlayEnvironmentAsync 同口径）
                var playRoot = FindPlayRoot();
                playRoot.SetActive(true);
                await WaitUntil(() => UIManager.HasInstance, BootstrapTimeoutSec, "框架自举", overallDeadline: deadline);
                var manager = playRoot.GetComponent<GameManager>();

                // ③ 生产链路起局：试玩模式 + 内存关卡注入（GameManager 真实 Initialize 流程）
                manager.EnterPlayTestMode(levelData);
                await manager.InitGameWithLevelDataAsync(levelData, levelData.levelId);
                await WaitUntil(() => manager.State == GameLoadState.Ready, LevelReadyTimeoutSec, "关卡就绪", overallDeadline: deadline);

                var pigs = ClassicWorld.Active.Pigs;
                var flow = ClassicWorld.Active.Flow;

                // ④ 初始语义状态（第 0 步；报告从游戏本体读取）
                report.initial = new StepRecord { semanticState = SemanticStateOf(pigs, flow), semanticStateHash = "" };
                report.initial.semanticStateHash = HashText(report.initial.semanticState);

                // ⑤ 逐步执行磁带动作：真实点击入口 → 等落定 → 记录实际语义状态
                var tape = JsonUtility.FromJson<TapeFile>(await File.ReadAllTextAsync(tapePath));
                foreach (var step in tape.steps ?? Array.Empty<TapeStep>())
                {
                    var pig = pigs.SpawnedPigs.FirstOrDefault(p => p.PigData != null && p.PigData.Id == step.move.pigId);
                    if (pig == null)
                        throw new InvalidOperationException($"第 {step.index} 步：猪 #{step.move.pigId} 不在场（已离场或未生成）");

                    pig.OnPointerClick(null);   // 玩家同一入口（真实输入路径——假对拍检查点）
                    var stepOk = await WaitForSettlement(pigs, flow, SettleTimeoutSec, deadline);
                    if (!stepOk)
                        throw new InvalidOperationException($"第 {step.index} 步落定超时");

                    var record = new StepRecord { index = step.index };
                    record.semanticState = SemanticStateOf(pigs, flow);
                    record.semanticStateHash = HashText(record.semanticState);
                    record.outcome = OutcomeOf(flow);
                    report.steps.Add(record);

                    // 胜负已定即可停（后续步无需执行；末局状态已记录）
                    if (flow.ResultState == GameResultState.Won || flow.ResultState == GameResultState.Failed)
                        break;
                }

                // ⑥ 结局：读游戏自身判定
                report.outcome = OutcomeOf(flow);
                report.finalSemanticState = SemanticStateOf(pigs, flow);
            }
            catch (Exception ex)
            {
                report.outcome = "error";
                report.error = ex.Message;
                exitCode = 1;   // harness 自身失败（与游戏胜负无关）
            }
            finally
            {
                SessionState.EraseBool(SKeyPending);   // 清除续跑标记（幂等：正常/异常路径都清）
                try
                {
                    await File.WriteAllTextAsync(outPath, JsonUtility.ToJson(report));
                }
                catch (Exception writeEx)
                {
                    Debug.LogError("[LevelForgeHarness] 写结果失败: " + writeEx.Message);
                    exitCode = 4;
                }
                EditorApplication.Exit(exitCode);
            }
        }

        // 场景内查找玩法环境根（挂 GameManager 的对象；LevelPlayTestHost.playRoot 的等价物）
        static GameObject FindPlayRoot()
        {
            var manager = UnityEngine.Object.FindObjectOfType<GameManager>(true);
            if (manager != null)
                return manager.gameObject;
            throw new InvalidOperationException("场景中未找到 GameManager（playRoot）");
        }

        // 语义状态（与 SlidePigAdapter.ProjectSemanticState 同 schema，且直接按平台 CanonicalJson 规范形态输出：
        // 对象键按字母序（failed<pigs；cells<counter<facing<id<type；x<y），pigs 按 id 升序、cells 按 x,y 升序）
        static string SemanticStateOf(PigManager pigs, GameManager flow)
        {
            var sb = new StringBuilder(1024);
            sb.Append("{\"failed\":").Append(flow.ResultState == GameResultState.Failed ? "true" : "false").Append(",\"pigs\":[");
            var ordered = pigs.SpawnedPigs.Where(p => p.PigData != null).OrderBy(p => p.PigData.Id).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var p = ordered[i];
                sb.Append(i > 0 ? "," : "")
                    .Append("{\"cells\":[");
                var cells = p.OccupiedCells.OrderBy(c => c.x).ThenBy(c => c.y).ToList();
                for (var c = 0; c < cells.Count; c++)
                {
                    sb.Append(c > 0 ? "," : "").Append("{\"x\":").Append(cells[c].x).Append(",\"y\":").Append(cells[c].y).Append('}');
                }
                sb.Append("],\"counter\":").Append(CounterOf(p))
                    .Append(",\"facing\":\"").Append(p.PigData.Direction)
                    .Append("\",\"id\":").Append(p.PigData.Id)
                    .Append(",\"type\":\"").Append(p.PigData.PigType)
                    .Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // 计数：爆炸猪/锁猪/铁笼的剩余次数；其余 0（与适配器 Counter 口径一致）
        static int CounterOf(NormalPig p)
        {
            if (p is BoomPig boom) return boom.RemainBoomCount;
            if (p is LockPig lockPig) return lockPig.RemainUnlockCount;
            if (p is Cage cage) return cage.RemainUnlockCount;
            return 0;
        }

        // 结局字符串：win/lose/incomplete/error（读游戏自身判定；试玩模式不发布正式事件但 ResultState 仍推进）
        static string OutcomeOf(GameManager flow)
        {
            if (flow.ResultState == GameResultState.Won) return "win";
            if (flow.ResultState == GameResultState.Failed) return "lose";
            return "incomplete";
        }

        // 结算等待：逐帧轮询棋盘落定（PigManager.IsBoardSettled）+ 玩法就绪；禁固定 sleep——假对拍检查点
        static async UniTask<bool> WaitForSettlement(PigManager pigs, GameManager flow, double timeoutSec, double overallDeadline)
        {
            return await WaitUntil(() => pigs.IsBoardSettled() && flow.State == GameLoadState.Ready,
                timeoutSec, "棋盘落定", tolerateTimeout: true, overallDeadline: overallDeadline);
        }

        // 带超时的逐帧等待；超时可容忍时返回 false，否则抛异常
        static async UniTask<bool> WaitUntil(Func<bool> predicate, double timeoutSec, string what,
            bool tolerateTimeout = true, double? overallDeadline = null)
        {
            var deadline = EditorApplication.timeSinceStartup + timeoutSec;
            while (!predicate())
            {
                var now = EditorApplication.timeSinceStartup;
                if (now > deadline || (overallDeadline is { } od && now > od))
                {
                    if (tolerateTimeout) return false;
                    throw new TimeoutException($"等待超时：{what}（{timeoutSec:F0}s）");
                }
                await UniTask.Yield();
            }
            return true;
        }

        // 语义哈希：与平台 TapeHashes.SemanticStateHash 同口径——sha256(CanonicalJson({"semanticSchemaVer":N,"state":S}))
        // SemanticStateOf 已按规范化形态输出（键序/数组序对齐），此处仅需组payload再哈希
        static string HashText(string canonicalStateJson)
        {
            var payload = "{\"semanticSchemaVer\":" + 1 + ",\"state\":" + canonicalStateJson + "}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            return "sha256:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "").ToLowerInvariant();
        }

        // 文件字节 sha256（harness 自身指纹）
        static string HashFile(string path)
        {
            if (!File.Exists(path))
                return "sha256:missing";
            using var sha = System.Security.Cryptography.SHA256.Create();
            return "sha256:" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        // 对拍结果（result.json；结构对齐接入套件skill详设 §4.4 协议字段）
        [Serializable]
        public class Report
        {
            public string levelRef;
            public string levelHash;                 // 由平台侧核对请求方填入（编辑器侧无磁带哈希上下文）
            public string gameBuildFingerprint;
            public string harnessHash;
            public int moveSchemaVer;
            public int semanticSchemaVer;
            public string executionEnvironment;
            public StepRecord initial;
            public List<StepRecord> steps = new();
            public string outcome;
            public string finalSemanticState;
            public string error;
        }

        [Serializable]
        public class StepRecord
        {
            public int index;
            public string semanticState;
            public string semanticStateHash;
            public string outcome;
        }
    }
}
