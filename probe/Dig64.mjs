// 000064 分歧勘查：第46步点击 pig69 前后的列5占用（临时诊断）
import { readFileSync } from 'node:fs';
const t = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const pre = t.steps[44].semanticState;
const post = t.steps[45].semanticState;
for (const tag of ['pre', 'post'])
{
    const s = tag === 'pre' ? pre : post;
    const p69 = s.pigs.find(p => p.id === 69);
    console.log(tag, 'pig69:', JSON.stringify(p69));
}
console.log('pre column-5 (y ascending):');
for (const p of pre.pigs.filter(p => p.cells.some(c => c.x === 5)).sort((a, b) => a.cells[0].y - b.cells[0].y))
    console.log(' id', p.id, p.type, 'facing', p.facing, 'counter', p.counter, JSON.stringify(p.cells));
