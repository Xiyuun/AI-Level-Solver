// 脚印推导规则验证：扫描全部支持关卡，检验"头格+朝向+类型 → 脚印集合"的推导规则（临时诊断）
// 规则假设：facing 纵轴(Up/Down)→竖排、横轴(Left/Right)→横排；
// Normal/Boom/Spring/Lock/Cage=2格、Long=3格、SmallElephant=2×3块、CageL=5×5缺左上3×3(16格)
// 小象的朝向与形状关系、Cage 朝向待数据验证
import { readFileSync, readdirSync } from 'node:fs';

const dir = 'E:/Projects/UnityProjects/PiggyFarm/piggyfarm/Assets/Main/FrameWorkExtras/LevelData/Luban~/Datas/Levels';
const LEN = { Normal: 2, Boom: 2, Spring: 2, Lock: 2, Cage: 2, Long: 3, SmallElephant: 6, CageL: 16 };

const stats = {};   // type -> {count, verticalVsFacing:{Up:竖/横…}, smallElephantShapes:{}}
let violations = 0;
for (const f of readdirSync(dir).filter(x => x.endsWith('.json')))
{
    const lv = JSON.parse(readFileSync(`${dir}/${f}`, 'utf8'));
    for (const p of lv.pigs ?? [])
    {
        if (!(p.pigType in LEN)) continue;
        const s = (stats[p.pigType] ??= { count: 0, lenOk: 0, facingAxis: {}, small: {} });
        s.count++;
        if (p.occupiedCells.length === LEN[p.pigType]) s.lenOk++;
        else if (violations < 5) { console.log(`LEN Violation ${f} pig#${p.id} ${p.pigType} ${p.occupiedCells.length} cells`); violations++; }

        // 纵横判定：所有格 x 相同=竖排、y 相同=横排
        const xs = new Set(p.occupiedCells.map(c => c.x));
        const ys = new Set(p.occupiedCells.map(c => c.y));
        const orient = xs.size === 1 ? 'vertical' : ys.size === 1 ? 'horizontal' : `irregular(${xs.size}x${ys.size})`;
        const verticalFacing = p.facing === 'Up' || p.facing === 'Down';
        const axisMatch = (orient === 'vertical') === verticalFacing ? 'match' : 'MISMATCH';
        if (p.pigType !== 'SmallElephant' && p.pigType !== 'CageL')
        {
            s.facingAxis[axisMatch + ':' + p.facing + ':' + orient] = (s.facingAxis[axisMatch + ':' + p.facing + ':' + orient] ?? 0) + 1;
        }
        if (p.pigType === 'SmallElephant')
        {
            const w = xs.size, h = ys.size;
            const key = `${w}x${h}|facing=${p.facing}`;
            s.small[key] = (s.small[key] ?? 0) + 1;
        }
        if (p.pigType === 'CageL')
        {
            const minX = Math.min(...p.occupiedCells.map(c => c.x));
            const minY = Math.min(...p.occupiedCells.map(c => c.y));
            const maxX = Math.max(...p.occupiedCells.map(c => c.x));
            const maxY = Math.max(...p.occupiedCells.map(c => c.y));
            // 5x5 缺左上 3x3：bbox 5x5，缺格 = x∈[minX,minX+2] 且 y∈[minY,minY+2]
            const missing = [];
            for (let x = minX; x <= maxX; x++) for (let y = minY; y <= maxY; y++)
            {
                if (!p.occupiedCells.some(c => c.x === x && c.y === y)) missing.push(`${x},${y}`);
            }
            const expected = [];
            for (let x = minX; x <= minX + 2; x++) for (let y = minY; y <= minY + 2; y++) expected.push(`${x},${y}`);
            const isL = maxX - minX === 4 && maxY - minY === 4 && missing.sort().join(';') === expected.sort().join(';');
            if (!isL && violations < 8) { console.log(`CageL Violation ${f} pig#${p.id} bbox ${maxX - minX + 1}x${maxY - minY + 1} missing=[${missing}]`); violations++; }
        }
    }
}
for (const [t, s] of Object.entries(stats))
{
    console.log(`\n${t}: ${s.count} 只（长度符合 ${s.lenOk}）`);
    for (const [k, v] of Object.entries(s.facingAxis ?? {})) console.log(`  ${k}: ${v}`);
    for (const [k, v] of Object.entries(s.small ?? {})) console.log(`  小象 ${k}: ${v}`);
}
