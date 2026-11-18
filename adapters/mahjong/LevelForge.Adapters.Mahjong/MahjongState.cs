namespace LevelForge.Adapters.Mahjong;

// 特殊棋子类型（与游戏 ChessSpecialType 枚举一致）
public static class MahjongSpecial
{
    public const int Normal = 0;
    public const int Ice = 1;     // 冰冻：specialInfo>0 时不可拖/不可消/不可推；每次消除事件计数递减，归零转普通
    public const int Pop = 2;     // 爆竹：消除时炸随机同 type 对（未播种随机）——求解抽象按普通棋处理
    public const int Cont = 3;    // 连排：同 specialInfo 为一组，整组刚性同位移
    public const int Wall = 4;    // 墙体：不可拖/不可消/不可推，不计入清盘胜利条件
    public const int Flip = 5;    // 翻面：specialInfo 0 背面/1 正面——求解抽象按普通棋处理
}

// 单只棋子的规则状态（运行时 Chess.ts 字段同构；Pieces 列表恒按 Id 升序）
public sealed record MahjongPiece(int Id, int X, int Y, int Type, int SpecialType, int SpecialInfo);

// 墙体障碍：依附格 (x,y) 的 dir 方向夹缝（1 上/2 下/3 左/4 右）
public sealed record MahjongWall(int X, int Y, int Dir);

// 规则状态：棋盘尺寸 + 棋子列表（Id 升序不变式）+ 墙体 + 关卡元数据
// 不可变类；ApplyMove 返回全新实例（契约纯函数铁律）。
// 集合成员为值语义：手写 Equals/GetHashCode 逐元素比较（List 的引用相等不满足"逻辑相等 ⇔ 相等"）。
public sealed class MahjongState
{
    public int LevelId { get; }
    public int Difficulty { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<MahjongPiece> Pieces { get; }
    public IReadOnlyList<MahjongWall> Walls { get; }

    public MahjongState(
        int levelId, int difficulty, int width, int height,
        IReadOnlyList<MahjongPiece> pieces, IReadOnlyList<MahjongWall> walls)
    {
        LevelId = levelId;
        Difficulty = difficulty;
        Width = width;
        Height = height;
        Pieces = pieces;
        Walls = walls;
    }

    // 派生助手：替换棋子列表（其余字段保持）——等价于 record 的 with 表达式
    public MahjongState With(IReadOnlyList<MahjongPiece> pieces) => new(LevelId, Difficulty, Width, Height, pieces, Walls);

    // 派生助手：替换墙体列表
    public MahjongState WithWalls(IReadOnlyList<MahjongWall> walls) => new(LevelId, Difficulty, Width, Height, Pieces, walls);

    public bool Equals(MahjongState? other)
        => other is not null && LevelId == other.LevelId && Difficulty == other.Difficulty
            && Width == other.Width && Height == other.Height
            && Pieces.SequenceEqual(other.Pieces) && Walls.SequenceEqual(other.Walls);

    public override bool Equals(object? obj) => Equals(obj as MahjongState);

    public override int GetHashCode()
    {
        var hash = new System.HashCode();
        hash.Add(LevelId);
        hash.Add(Difficulty);
        hash.Add(Width);
        hash.Add(Height);
        foreach (var p in Pieces)
        {
            hash.Add(p.Id);
            hash.Add(p.X);
            hash.Add(p.Y);
            hash.Add(p.Type);
            hash.Add(p.SpecialType);
            hash.Add(p.SpecialInfo);
        }
        foreach (var w in Walls)
        {
            hash.Add(w.X);
            hash.Add(w.Y);
            hash.Add(w.Dir);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(MahjongState? left, MahjongState? right)
        => left?.Equals(right) ?? right is null;

    public static bool operator !=(MahjongState? left, MahjongState? right)
        => !(left == right);
}

// 结构化操作：源棋子拖动（或原位点击）后与目标棋子配对消除
// 与游戏官方可解求解器 LevelSolvabilitySolver.enumerateMoves 的走法三元组一字不差
public sealed record MahjongMove(int SourceId, int TargetId, int LandingX, int LandingY);
