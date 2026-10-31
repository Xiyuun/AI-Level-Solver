# test-template/ · 规则测试模板（通用用例骨架）

> 每接口一组骨架（xUnit，与平台一致）。放进 `adapter.tests/` 后把 `<Adapter>` / 状态构造换成项目实现。
> 骨架之外，项目特有机制每条至少一个边界用例（onboarding.md 步骤 3）。

## 1. LoadLevel

```csharp
[Fact]
public void LoadLevel_合法关卡_产出初始状态()
{
    var state = LoadLevel(合法关卡样本);
    Assert.Equal(预期的初始实体数, CountEntities(state));
}

[Theory]
[InlineData(缺玩家)]
[InlineData(实体数不符)]
[InlineData(格式损坏)]
public void LoadLevel_非法关卡_必须抛异常且不静默修复(坏样本样本号)
{
    Assert.ThrowsAny<Exception>(() => LoadLevel(坏样本));
}
```

## 2. GetLegalMoves

```csharp
[Fact]
public void GetLegalMoves_一般局面_非空且顺序稳定()
{
    var state = LoadLevel(一般样本);
    var first = Adapter.GetLegalMoves(state);
    var second = Adapter.GetLegalMoves(state);
    Assert.True(first.Count > 0);
    Assert.Equal(first, second);   // 多次调用一致（顺序稳定契约）
}

[Fact]
public void GetLegalMoves_全阻挡局面_返回空列表() { /* 构造四周阻挡 */ }
```

## 3. ApplyMove

```csharp
[Fact]
public void ApplyMove_纯函数_入参不被修改()
{
    var state = LoadLevel(样本);
    var before = 深拷贝语义快照(state);      // 完整语义状态，不能只比 StateKey
    var move = Adapter.GetLegalMoves(state)[0];
    _ = Adapter.ApplyMove(state, move);
    Assert.Equal(before, 深拷贝语义快照(state));
}

[Fact]
public void ApplyMove_确定性_同输入同输出()
{
    var state = LoadLevel(样本);
    var move = Adapter.GetLegalMoves(state)[0];
    Assert.Equal(语义投影(Adapter.ApplyMove(state, move)), 语义投影(Adapter.ApplyMove(state, move)));
}

[Fact]
public void ApplyMove_非法操作_抛异常() { /* 不在本状态 GetLegalMoves 中的 move */ }
```

## 4. IsGoal

```csharp
[Fact]
public void IsGoal_初始非终局_返回false() { /* 一般样本 */ }

[Fact]
public void IsGoal_构造终局_返回true()
{
    var state = 构造全部目标达成的状态();   // 直接构造，不靠搜索
    Assert.True(Adapter.IsGoal(state));
}
```

## 5. StateKey 维度判别生成器（按维度清单自动生成"仅该维度不同"的状态对）

```csharp
[Theory]
[InlineData("玩家位置")]
[InlineData("箱子位置")]
[InlineData("朝向")]            // ← 项目状态维度枚举逐个列出
[InlineData("剩余步数")]
public void StateKey_维度判别_仅该维度不同的状态对键必须不同(string dimension)
{
    var (a, b) = 构造仅该维度不同的状态对(dimension);   // 生成器按维度清单构造
    Assert.NotEqual(Adapter.StateKey(a), Adapter.StateKey(b));
}
```

> 维度清单来自资格报告并经开发者确认；本组用例与 StateKey 的"维度覆盖对照表"互相校对。

## agent 补充要求

- 项目特有机制每条至少一个边界用例，与 checklist A 组及 rule-matrix 逐项关联
- 纯函数测试比较**完整语义状态及嵌套集合**，不能只比较 StateKey
- 确定性测试比较同版本语义轨迹，不把 artifactHash 当行为摘要
