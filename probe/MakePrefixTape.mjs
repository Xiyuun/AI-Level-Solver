// 构造截断磁带：原磁带前 k 步 + 追加一步 click pigId（临时诊断）
// 运行：node probe/MakePrefixTape.mjs <源tape.json> <k> <pigId> <输出tape.json>
import { readFileSync, writeFileSync } from 'node:fs';
const [src, kArg, pigArg, out] = process.argv.slice(2);
const t = JSON.parse(readFileSync(src, 'utf8'));
const k = parseInt(kArg, 10);
const steps = t.steps.slice(0, k);
steps.push({ index: k + 1, move: { type: 'click', pigId: parseInt(pigArg, 10) }, moveText: 'probe' });
writeFileSync(out, JSON.stringify({ ...t, steps, outcome: 'incomplete' }));
console.log('written', out, 'steps:', steps.length);
