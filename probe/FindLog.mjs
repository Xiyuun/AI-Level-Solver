// 在游戏源码中定位“贴靠格被占”日志与其所在方法（临时诊断）
import { readFileSync } from 'node:fs';
const file = 'E:/Projects/UnityProjects/PiggyFarm/piggyfarm/Assets/Main/Game/Modules/ClassicMode/Script/Manager/MainGame/PigManager.cs';
const lines = readFileSync(file, 'utf8').split('\n');
lines.forEach((l, i) =>
{
    if (l.includes('Debug.Log') && l.includes('贴靠'))
    {
        console.log('LOG LINE', i + 1, ':', l.trim().slice(0, 140));
        // 向上找方法签名
        for (let j = i; j > Math.max(0, i - 80); j--)
        {
            if (/^\s*(public|private|internal|protected)\s+.*\(.*\)/.test(lines[j]) && !lines[j].trim().startsWith('//'))
            {
                console.log('  IN METHOD @', j + 1, ':', lines[j].trim().slice(0, 110));
                break;
            }
        }
    }
});
