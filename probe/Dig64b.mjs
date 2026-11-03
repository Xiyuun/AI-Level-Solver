// 000064 分歧勘查2：harness 实报的第46步后列5布局 vs 磁带
import { readFileSync } from 'node:fs';
const r = JSON.parse(readFileSync(process.argv[2], 'utf8'));const t = JSON.parse(readFileSync(process.argv[3], 'utf8'));
const step = parseInt(process.argv[4], 10);

const hs = typeof r.steps[step - 1].semanticState === 'string'
    ? JSON.parse(r.steps[step - 1].semanticState)
    : r.steps[step - 1].semanticState;
const ts = t.steps[step - 1].semanticState;

for (const [tag, s] of [['harness', hs], ['tape', ts]])
{
    console.log(tag, 'column-5 after step', step, ':');
    for (const p of s.pigs.filter(p => p.cells.some(c => c.x === 5)).sort((a, b) => a.cells[0].y - b.cells[0].y))
        console.log('  id', p.id, p.type, 'facing', p.facing, JSON.stringify(p.cells));
}
console.log('clicked move:', JSON.stringify(t.steps[step - 1].move));
