// harness 报告内相邻两步的全量实体变化（找隐藏的同步移动）
import { readFileSync } from 'node:fs';
const r = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const step = parseInt(process.argv[3], 10);
const parse = s => typeof s === 'string' ? JSON.parse(s) : s;
const a = parse(r.steps[step - 2].semanticState);   // 上一步
const b = parse(r.steps[step - 1].semanticState);   // 本步
const am = new Map(a.pigs.map(p => [p.id, p]));
const bm = new Map(b.pigs.map(p => [p.id, p]));
for (const [id, p] of am) if (!bm.has(id)) console.log('消失 id', id, p.type, JSON.stringify(p.cells));
for (const [id, p] of bm)
{
    const q = am.get(id);
    if (!q) { console.log('新增 id', id, p.type, JSON.stringify(p.cells)); continue; }
    if (JSON.stringify(q.cells) !== JSON.stringify(p.cells))
        console.log('移动 id', id, q.type, 'facing', q.facing, ':', JSON.stringify(q.cells), '->', JSON.stringify(p.cells));
    if (q.counter !== p.counter) console.log('计数 id', id, ':', q.counter, '->', p.counter);
}
console.log('clicked:', process.argv[4] || '(from tape)');
