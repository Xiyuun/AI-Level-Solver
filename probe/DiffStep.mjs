// 分歧现场勘查：指定关的第 N 步，harness 实报 vs 磁带预期的语义状态逐字段对比（临时诊断工具）
// 运行：node probe/DiffStep.mjs <result.json> <tape.json> <stepIndex>
import { readFileSync } from 'node:fs';

const [resultPath, tapePath, stepArg] = process.argv.slice(2);
const step = parseInt(stepArg, 10);
const r = JSON.parse(readFileSync(resultPath, 'utf8'));
const t = JSON.parse(readFileSync(tapePath, 'utf8'));

const rs = r.steps[step - 1];
const ts = t.steps[step - 1];
console.log('step', step, '| move:', JSON.stringify(ts.move), '| outcome h/t:', rs.outcome, '(tape win=' + t.outcome + ')');

const hState = typeof rs.semanticState === 'string' ? JSON.parse(rs.semanticState) : rs.semanticState;
const tState = ts.semanticState;
console.log('failed  h/t:', hState.failed, '/', tState.failed);
console.log('pigCount h/t:', hState.pigs.length, '/', tState.pigs.length);

const hMap = new Map(hState.pigs.map(p => [p.id, p]));
const tMap = new Map(tState.pigs.map(p => [p.id, p]));
for (const [id, tp] of tMap)
{
    const hp = hMap.get(id);
    if (!hp) { console.log(`pig#${id} 不在harness中(已离场?) tape=${JSON.stringify(tp)}`); continue; }
    if (hp.facing !== tp.facing) console.log(`pig#${id} facing: h=${hp.facing} t=${tp.facing}`);
    if (hp.type !== tp.type) console.log(`pig#${id} type: h=${hp.type} t=${tp.type}`);
    if (hp.counter !== tp.counter) console.log(`pig#${id} counter: h=${hp.counter} t=${tp.counter}`);
    const hc = JSON.stringify(hp.cells), tc = JSON.stringify(tp.cells);
    if (hc !== tc) console.log(`pig#${id} cells: h=${hc} t=${tc}`);
}
for (const id of hMap.keys()) if (!tMap.has(id)) console.log(`pig#${id} harness独有: ${JSON.stringify(hMap.get(id))}`);

// 也看上一步（分可能导致这一步的走法不同）
if (step >= 2)
{
    const prev = t.steps[step - 2];
    console.log('上一步 move:', JSON.stringify(prev.move));
}
