// 对拍比对：result.json（harness 实报）vs tape.json（适配层预期）逐步语义哈希（临时诊断工具）
// 运行：node probe/CompareTape.mjs <result.json> <tape.json>
import { readFileSync } from 'node:fs';

const [resultPath, tapePath] = process.argv.slice(2);
const r = JSON.parse(readFileSync(resultPath, 'utf8'));
const t = JSON.parse(readFileSync(tapePath, 'utf8'));

console.log('outcome:', r.outcome, '| error:', r.error ?? '无');
console.log('harness steps:', (r.steps ?? []).length, '| tape steps:', (t.steps ?? []).length);

// 初始帧（第 0 步）
const initMatch = r.initial?.semanticStateHash === t.initial?.semanticStateHash;
console.log('initial(第0步) hash match:', initMatch);
if (!initMatch)
{
    console.log('  harness:', r.initial?.semanticStateHash);
    console.log('  tape  :', t.initial?.semanticStateHash);
    // 找出第一个差异字段：解析两个语义状态对比
    try
    {
        const hs = JSON.parse(r.initial.semanticState);
        const ts = JSON.parse(t.initial.semanticState);
        const hmap = new Map(hs.pigs.map(p => [p.id, p]));
        for (const tp of ts.pigs)
        {
            const hp = hmap.get(tp.id);
            if (!hp) { console.log(`  猪#${tp.id} 不在harness报文中`); continue; }
            if (hp.facing !== tp.facing) console.log(`  猪#${tp.id} facing: harness=${hp.facing} tape=${tp.facing}`);
            if (hp.type !== tp.type) console.log(`  猪#${tp.id} type: harness=${hp.type} tape=${tp.type}`);
            if (JSON.stringify(hp.cells) !== JSON.stringify(tp.cells)) console.log(`  猪#${tp.id} cells: harness=${JSON.stringify(hp.cells)} tape=${JSON.stringify(tp.cells)}`);
        }
    }
    catch (e) { console.log('  语义状态解析失败：', e.message); }
}

// 逐步比对
let firstDivergence = -1;
const n = Math.min((r.steps ?? []).length, (t.steps ?? []).length);
for (let i = 0; i < n; i++)
{
    const match = r.steps[i].semanticStateHash === t.steps[i].semanticStateHash;
    console.log(`step ${i + 1}: match=${match} outcome=${r.steps[i].outcome ?? ''} move=${JSON.stringify(t.steps[i].move)}`);
    if (!match && firstDivergence < 0) firstDivergence = i + 1;
}
console.log(firstDivergence > 0 ? `==== 首分歧步：${firstDivergence} ====` : '==== 逐步语义一致 ====');
