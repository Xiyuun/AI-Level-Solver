using System.Text.Json.Nodes;

namespace LevelForge.Adapters.PiggyFarm;

// 真实关卡模板库（12×19，v1 子集：Normal/Long/RichPig，无地形/特殊猪）：
// 2 档用项目原生 difficulty=2 且求解器可解的 374；3 档用原生 difficulty=3 的 19/29/59，另加 112 猪密集剥离模板。
// 生成时只做对称变换/重编号，关卡设计本身来自项目真实内容，保证挑战性。
public static class PiggyFarmTemplates
{
    public const int Width = 12;
    public const int Height = 19;

    public const string Level374 = """
{
    "levelId": 374,
    "difficulty": 2,
    "levelName": "nlevel 374",
    "mapWidth": 12,
    "mapHeight": 19,
    "pigCount": 77,
    "pigs": [
        {
            "id": 0,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 6
                },
                {
                    "x": 6,
                    "y": 7
                }
            ]
        },
        {
            "id": 1,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 5
                },
                {
                    "x": 8,
                    "y": 5
                }
            ]
        },
        {
            "id": 2,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 10
                },
                {
                    "x": 6,
                    "y": 9
                }
            ]
        },
        {
            "id": 3,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 5
                },
                {
                    "x": 4,
                    "y": 5
                }
            ]
        },
        {
            "id": 4,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 5
                },
                {
                    "x": 9,
                    "y": 4
                }
            ]
        },
        {
            "id": 5,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 13
                },
                {
                    "x": 6,
                    "y": 12
                }
            ]
        },
        {
            "id": 6,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 5
                },
                {
                    "x": 2,
                    "y": 6
                }
            ]
        },
        {
            "id": 7,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 7
                },
                {
                    "x": 1,
                    "y": 7
                }
            ]
        },
        {
            "id": 8,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 13
                },
                {
                    "x": 7,
                    "y": 12
                }
            ]
        },
        {
            "id": 9,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 13
                },
                {
                    "x": 8,
                    "y": 12
                }
            ]
        },
        {
            "id": 10,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 7
                },
                {
                    "x": 10,
                    "y": 7
                }
            ]
        },
        {
            "id": 11,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 9
                },
                {
                    "x": 10,
                    "y": 8
                }
            ]
        },
        {
            "id": 12,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 11
                },
                {
                    "x": 10,
                    "y": 10
                }
            ]
        },
        {
            "id": 13,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 8
                },
                {
                    "x": 2,
                    "y": 9
                }
            ]
        },
        {
            "id": 14,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 11
                },
                {
                    "x": 2,
                    "y": 10
                }
            ]
        },
        {
            "id": 15,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 12
                },
                {
                    "x": 11,
                    "y": 12
                }
            ]
        },
        {
            "id": 16,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 12
                },
                {
                    "x": 4,
                    "y": 12
                }
            ]
        },
        {
            "id": 17,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 13
                },
                {
                    "x": 4,
                    "y": 13
                }
            ]
        },
        {
            "id": 18,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 5
                },
                {
                    "x": 1,
                    "y": 6
                }
            ]
        },
        {
            "id": 19,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 4
                },
                {
                    "x": 2,
                    "y": 4
                }
            ]
        },
        {
            "id": 20,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 14
                },
                {
                    "x": 5,
                    "y": 14
                }
            ]
        },
        {
            "id": 21,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 8
                },
                {
                    "x": 1,
                    "y": 9
                }
            ]
        },
        {
            "id": 22,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 13
                },
                {
                    "x": 9,
                    "y": 13
                }
            ]
        },
        {
            "id": 23,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 12
                },
                {
                    "x": 2,
                    "y": 12
                }
            ]
        },
        {
            "id": 24,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 10
                },
                {
                    "x": 1,
                    "y": 11
                }
            ]
        },
        {
            "id": 25,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 3
                },
                {
                    "x": 11,
                    "y": 3
                }
            ]
        },
        {
            "id": 26,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 13
                },
                {
                    "x": 2,
                    "y": 13
                }
            ]
        },
        {
            "id": 27,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 14
                },
                {
                    "x": 3,
                    "y": 13
                }
            ]
        },
        {
            "id": 28,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 4
                },
                {
                    "x": 11,
                    "y": 5
                }
            ]
        },
        {
            "id": 29,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 3
                },
                {
                    "x": 2,
                    "y": 3
                }
            ]
        },
        {
            "id": 30,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 13
                },
                {
                    "x": 11,
                    "y": 14
                }
            ]
        },
        {
            "id": 31,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 10
                },
                {
                    "x": 11,
                    "y": 11
                }
            ]
        },
        {
            "id": 32,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 6
                },
                {
                    "x": 10,
                    "y": 6
                }
            ]
        },
        {
            "id": 33,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 8
                },
                {
                    "x": 11,
                    "y": 9
                }
            ]
        },
        {
            "id": 34,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 14
                },
                {
                    "x": 1,
                    "y": 15
                }
            ]
        },
        {
            "id": 35,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 15
                },
                {
                    "x": 2,
                    "y": 15
                }
            ]
        },
        {
            "id": 36,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 14
                },
                {
                    "x": 6,
                    "y": 14
                }
            ]
        },
        {
            "id": 37,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 15
                },
                {
                    "x": 8,
                    "y": 14
                }
            ]
        },
        {
            "id": 38,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 14
                },
                {
                    "x": 9,
                    "y": 14
                }
            ]
        },
        {
            "id": 39,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 15
                },
                {
                    "x": 10,
                    "y": 15
                }
            ]
        },
        {
            "id": 40,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 4
                },
                {
                    "x": 10,
                    "y": 5
                }
            ]
        },
        {
            "id": 41,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 2
                },
                {
                    "x": 10,
                    "y": 2
                }
            ]
        },
        {
            "id": 42,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 2
                },
                {
                    "x": 2,
                    "y": 2
                }
            ]
        },
        {
            "id": 43,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 4
                },
                {
                    "x": 3,
                    "y": 3
                }
            ]
        },
        {
            "id": 44,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 3
                },
                {
                    "x": 5,
                    "y": 3
                }
            ]
        },
        {
            "id": 45,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 1
                },
                {
                    "x": 5,
                    "y": 1
                }
            ]
        },
        {
            "id": 46,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 2
                },
                {
                    "x": 5,
                    "y": 2
                }
            ]
        },
        {
            "id": 47,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 2
                },
                {
                    "x": 6,
                    "y": 3
                }
            ]
        },
        {
            "id": 48,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 1
                },
                {
                    "x": 9,
                    "y": 1
                }
            ]
        },
        {
            "id": 49,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 1
                },
                {
                    "x": 7,
                    "y": 1
                }
            ]
        },
        {
            "id": 50,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 2
                },
                {
                    "x": 7,
                    "y": 2
                }
            ]
        },
        {
            "id": 51,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 2
                },
                {
                    "x": 9,
                    "y": 3
                }
            ]
        },
        {
            "id": 52,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 3
                },
                {
                    "x": 8,
                    "y": 4
                }
            ]
        },
        {
            "id": 53,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 3
                },
                {
                    "x": 7,
                    "y": 4
                }
            ]
        },
        {
            "id": 54,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 17
                },
                {
                    "x": 3,
                    "y": 16
                }
            ]
        },
        {
            "id": 55,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 17
                },
                {
                    "x": 5,
                    "y": 17
                }
            ]
        },
        {
            "id": 56,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 17
                },
                {
                    "x": 9,
                    "y": 17
                }
            ]
        },
        {
            "id": 57,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 17
                },
                {
                    "x": 7,
                    "y": 17
                }
            ]
        },
        {
            "id": 58,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 16
                },
                {
                    "x": 8,
                    "y": 16
                }
            ]
        },
        {
            "id": 59,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 16
                },
                {
                    "x": 6,
                    "y": 16
                }
            ]
        },
        {
            "id": 60,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 16
                },
                {
                    "x": 7,
                    "y": 15
                }
            ]
        },
        {
            "id": 61,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 16
                },
                {
                    "x": 4,
                    "y": 15
                }
            ]
        },
        {
            "id": 62,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 15
                },
                {
                    "x": 5,
                    "y": 15
                }
            ]
        },
        {
            "id": 63,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 4
                },
                {
                    "x": 6,
                    "y": 5
                }
            ]
        },
        {
            "id": 64,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 2
                },
                {
                    "x": 3,
                    "y": 1
                }
            ]
        },
        {
            "id": 65,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 11
                },
                {
                    "x": 9,
                    "y": 11
                }
            ]
        },
        {
            "id": 66,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 4
                },
                {
                    "x": 5,
                    "y": 5
                }
            ]
        },
        {
            "id": 67,
            "facing": "Up",
            "pigType": "RichPig",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 8
                },
                {
                    "x": 4,
                    "y": 9
                },
                {
                    "x": 4,
                    "y": 10
                },
                {
                    "x": 5,
                    "y": 8
                },
                {
                    "x": 5,
                    "y": 9
                },
                {
                    "x": 5,
                    "y": 10
                }
            ]
        },
        {
            "id": 68,
            "facing": "Up",
            "pigType": "RichPig",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 8
                },
                {
                    "x": 7,
                    "y": 9
                },
                {
                    "x": 7,
                    "y": 10
                },
                {
                    "x": 8,
                    "y": 8
                },
                {
                    "x": 8,
                    "y": 9
                },
                {
                    "x": 8,
                    "y": 10
                }
            ]
        },
        {
            "id": 69,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 6
                },
                {
                    "x": 7,
                    "y": 6
                }
            ]
        },
        {
            "id": 70,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 6
                },
                {
                    "x": 5,
                    "y": 6
                }
            ]
        },
        {
            "id": 71,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 11
                },
                {
                    "x": 4,
                    "y": 11
                }
            ]
        },
        {
            "id": 72,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 11
                },
                {
                    "x": 6,
                    "y": 11
                }
            ]
        },
        {
            "id": 73,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 9
                },
                {
                    "x": 3,
                    "y": 8
                }
            ]
        },
        {
            "id": 74,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 9
                },
                {
                    "x": 9,
                    "y": 8
                }
            ]
        },
        {
            "id": 75,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 7
                },
                {
                    "x": 4,
                    "y": 7
                }
            ]
        },
        {
            "id": 76,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 7
                },
                {
                    "x": 7,
                    "y": 7
                }
            ]
        }
    ]
}
""";

    public const string Level19 = """
{
    "levelId": 19,
    "difficulty": 3,
    "levelName": "nlevel 19",
    "mapWidth": 12,
    "mapHeight": 19,
    "pigCount": 89,
    "pigs": [
        {
            "id": 0,
            "facing": "Left",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 10
                },
                {
                    "x": 7,
                    "y": 10
                },
                {
                    "x": 8,
                    "y": 10
                }
            ]
        },
        {
            "id": 1,
            "facing": "Down",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 12
                },
                {
                    "x": 5,
                    "y": 11
                },
                {
                    "x": 5,
                    "y": 10
                }
            ]
        },
        {
            "id": 2,
            "facing": "Down",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 12
                },
                {
                    "x": 4,
                    "y": 11
                },
                {
                    "x": 4,
                    "y": 10
                }
            ]
        },
        {
            "id": 3,
            "facing": "Right",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 8
                },
                {
                    "x": 4,
                    "y": 8
                },
                {
                    "x": 3,
                    "y": 8
                }
            ]
        },
        {
            "id": 4,
            "facing": "Right",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 7
                },
                {
                    "x": 4,
                    "y": 7
                },
                {
                    "x": 3,
                    "y": 7
                }
            ]
        },
        {
            "id": 5,
            "facing": "Down",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 8
                },
                {
                    "x": 6,
                    "y": 7
                },
                {
                    "x": 6,
                    "y": 6
                }
            ]
        },
        {
            "id": 6,
            "facing": "Down",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 8
                },
                {
                    "x": 7,
                    "y": 7
                },
                {
                    "x": 7,
                    "y": 6
                }
            ]
        },
        {
            "id": 7,
            "facing": "Left",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 11
                },
                {
                    "x": 7,
                    "y": 11
                },
                {
                    "x": 8,
                    "y": 11
                }
            ]
        },
        {
            "id": 8,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 0
                },
                {
                    "x": 6,
                    "y": 1
                }
            ]
        },
        {
            "id": 9,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 0
                },
                {
                    "x": 7,
                    "y": 1
                }
            ]
        },
        {
            "id": 10,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 0
                },
                {
                    "x": 8,
                    "y": 1
                }
            ]
        },
        {
            "id": 11,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 1
                },
                {
                    "x": 9,
                    "y": 0
                }
            ]
        },
        {
            "id": 12,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 0
                },
                {
                    "x": 4,
                    "y": 0
                }
            ]
        },
        {
            "id": 13,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 1
                },
                {
                    "x": 4,
                    "y": 1
                }
            ]
        },
        {
            "id": 14,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 0
                },
                {
                    "x": 2,
                    "y": 0
                }
            ]
        },
        {
            "id": 15,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 1
                },
                {
                    "x": 2,
                    "y": 1
                }
            ]
        },
        {
            "id": 16,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 2
                },
                {
                    "x": 2,
                    "y": 2
                }
            ]
        },
        {
            "id": 17,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 3
                },
                {
                    "x": 2,
                    "y": 3
                }
            ]
        },
        {
            "id": 18,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 2
                },
                {
                    "x": 5,
                    "y": 2
                }
            ]
        },
        {
            "id": 19,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 3
                },
                {
                    "x": 5,
                    "y": 3
                }
            ]
        },
        {
            "id": 20,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 2
                },
                {
                    "x": 8,
                    "y": 2
                }
            ]
        },
        {
            "id": 21,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 3
                },
                {
                    "x": 8,
                    "y": 3
                }
            ]
        },
        {
            "id": 22,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 2
                },
                {
                    "x": 0,
                    "y": 3
                }
            ]
        },
        {
            "id": 23,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 2
                },
                {
                    "x": 3,
                    "y": 3
                }
            ]
        },
        {
            "id": 24,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 3
                },
                {
                    "x": 6,
                    "y": 2
                }
            ]
        },
        {
            "id": 25,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 2
                },
                {
                    "x": 11,
                    "y": 2
                }
            ]
        },
        {
            "id": 26,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 3
                },
                {
                    "x": 10,
                    "y": 4
                }
            ]
        },
        {
            "id": 27,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 3
                },
                {
                    "x": 11,
                    "y": 4
                }
            ]
        },
        {
            "id": 28,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 4
                },
                {
                    "x": 9,
                    "y": 3
                }
            ]
        },
        {
            "id": 29,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 5
                },
                {
                    "x": 8,
                    "y": 4
                }
            ]
        },
        {
            "id": 30,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 5
                },
                {
                    "x": 2,
                    "y": 4
                }
            ]
        },
        {
            "id": 31,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 4
                },
                {
                    "x": 0,
                    "y": 5
                }
            ]
        },
        {
            "id": 32,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 4
                },
                {
                    "x": 1,
                    "y": 5
                }
            ]
        },
        {
            "id": 33,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 4
                },
                {
                    "x": 4,
                    "y": 4
                }
            ]
        },
        {
            "id": 34,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 4
                },
                {
                    "x": 5,
                    "y": 4
                }
            ]
        },
        {
            "id": 35,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 5
                },
                {
                    "x": 10,
                    "y": 5
                }
            ]
        },
        {
            "id": 36,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 5
                },
                {
                    "x": 7,
                    "y": 5
                }
            ]
        },
        {
            "id": 37,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 6
                },
                {
                    "x": 5,
                    "y": 5
                }
            ]
        },
        {
            "id": 38,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 5
                },
                {
                    "x": 4,
                    "y": 6
                }
            ]
        },
        {
            "id": 39,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 6
                },
                {
                    "x": 3,
                    "y": 6
                }
            ]
        },
        {
            "id": 40,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 6
                },
                {
                    "x": 1,
                    "y": 6
                }
            ]
        },
        {
            "id": 41,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 7
                },
                {
                    "x": 0,
                    "y": 7
                }
            ]
        },
        {
            "id": 42,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 8
                },
                {
                    "x": 1,
                    "y": 8
                }
            ]
        },
        {
            "id": 43,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 9
                },
                {
                    "x": 1,
                    "y": 9
                }
            ]
        },
        {
            "id": 44,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 6
                },
                {
                    "x": 9,
                    "y": 6
                }
            ]
        },
        {
            "id": 45,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 8
                },
                {
                    "x": 0,
                    "y": 9
                }
            ]
        },
        {
            "id": 46,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 6
                },
                {
                    "x": 11,
                    "y": 7
                }
            ]
        },
        {
            "id": 47,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 8
                },
                {
                    "x": 9,
                    "y": 7
                }
            ]
        },
        {
            "id": 48,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 9
                },
                {
                    "x": 10,
                    "y": 8
                }
            ]
        },
        {
            "id": 49,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 9
                },
                {
                    "x": 11,
                    "y": 8
                }
            ]
        },
        {
            "id": 50,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 9
                },
                {
                    "x": 8,
                    "y": 9
                }
            ]
        },
        {
            "id": 51,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 11
                },
                {
                    "x": 9,
                    "y": 10
                }
            ]
        },
        {
            "id": 52,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 12
                },
                {
                    "x": 10,
                    "y": 11
                }
            ]
        },
        {
            "id": 53,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 11
                },
                {
                    "x": 11,
                    "y": 10
                }
            ]
        },
        {
            "id": 54,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 13
                },
                {
                    "x": 11,
                    "y": 12
                }
            ]
        },
        {
            "id": 55,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 10
                },
                {
                    "x": 1,
                    "y": 10
                }
            ]
        },
        {
            "id": 56,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 11
                },
                {
                    "x": 1,
                    "y": 11
                }
            ]
        },
        {
            "id": 57,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 11
                },
                {
                    "x": 2,
                    "y": 10
                }
            ]
        },
        {
            "id": 58,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 12
                },
                {
                    "x": 0,
                    "y": 13
                }
            ]
        },
        {
            "id": 59,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 12
                },
                {
                    "x": 1,
                    "y": 13
                }
            ]
        },
        {
            "id": 60,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 13
                },
                {
                    "x": 3,
                    "y": 13
                }
            ]
        },
        {
            "id": 61,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 13
                },
                {
                    "x": 5,
                    "y": 13
                }
            ]
        },
        {
            "id": 62,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 12
                },
                {
                    "x": 2,
                    "y": 12
                }
            ]
        },
        {
            "id": 63,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 13
                },
                {
                    "x": 6,
                    "y": 13
                }
            ]
        },
        {
            "id": 64,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 14
                },
                {
                    "x": 6,
                    "y": 14
                }
            ]
        },
        {
            "id": 65,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 13
                },
                {
                    "x": 9,
                    "y": 13
                }
            ]
        },
        {
            "id": 66,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 12
                },
                {
                    "x": 8,
                    "y": 12
                }
            ]
        },
        {
            "id": 67,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 14
                },
                {
                    "x": 8,
                    "y": 13
                }
            ]
        },
        {
            "id": 68,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 15
                },
                {
                    "x": 9,
                    "y": 14
                }
            ]
        },
        {
            "id": 69,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 14
                },
                {
                    "x": 10,
                    "y": 14
                }
            ]
        },
        {
            "id": 70,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 15
                },
                {
                    "x": 10,
                    "y": 15
                }
            ]
        },
        {
            "id": 71,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 15
                },
                {
                    "x": 7,
                    "y": 15
                }
            ]
        },
        {
            "id": 72,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 15
                },
                {
                    "x": 5,
                    "y": 15
                }
            ]
        },
        {
            "id": 73,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 14
                },
                {
                    "x": 1,
                    "y": 14
                }
            ]
        },
        {
            "id": 74,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 15
                },
                {
                    "x": 0,
                    "y": 16
                }
            ]
        },
        {
            "id": 75,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 15
                },
                {
                    "x": 1,
                    "y": 16
                }
            ]
        },
        {
            "id": 76,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 16
                },
                {
                    "x": 3,
                    "y": 16
                }
            ]
        },
        {
            "id": 77,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 16
                },
                {
                    "x": 5,
                    "y": 16
                }
            ]
        },
        {
            "id": 78,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 16
                },
                {
                    "x": 7,
                    "y": 16
                }
            ]
        },
        {
            "id": 79,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 14
                },
                {
                    "x": 3,
                    "y": 15
                }
            ]
        },
        {
            "id": 80,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 17
                },
                {
                    "x": 3,
                    "y": 18
                }
            ]
        },
        {
            "id": 81,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 18
                },
                {
                    "x": 2,
                    "y": 17
                }
            ]
        },
        {
            "id": 82,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 17
                },
                {
                    "x": 4,
                    "y": 17
                }
            ]
        },
        {
            "id": 83,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 17
                },
                {
                    "x": 6,
                    "y": 17
                }
            ]
        },
        {
            "id": 84,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 18
                },
                {
                    "x": 5,
                    "y": 18
                }
            ]
        },
        {
            "id": 85,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 18
                },
                {
                    "x": 9,
                    "y": 18
                }
            ]
        },
        {
            "id": 86,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 17
                },
                {
                    "x": 8,
                    "y": 16
                }
            ]
        },
        {
            "id": 87,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 17
                },
                {
                    "x": 9,
                    "y": 16
                }
            ]
        },
        {
            "id": 88,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 16
                },
                {
                    "x": 11,
                    "y": 16
                }
            ]
        }
    ]
}
""";

    public const string Level29 = """
{
    "levelId": 29,
    "difficulty": 3,
    "levelName": "nlevel 29",
    "mapWidth": 12,
    "mapHeight": 19,
    "pigCount": 79,
    "pigs": [
        {
            "id": 0,
            "facing": "Left",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 14
                },
                {
                    "x": 5,
                    "y": 14
                },
                {
                    "x": 6,
                    "y": 14
                }
            ]
        },
        {
            "id": 1,
            "facing": "Right",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 8
                },
                {
                    "x": 8,
                    "y": 8
                },
                {
                    "x": 7,
                    "y": 8
                }
            ]
        },
        {
            "id": 2,
            "facing": "Up",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 3
                },
                {
                    "x": 5,
                    "y": 4
                },
                {
                    "x": 5,
                    "y": 5
                }
            ]
        },
        {
            "id": 3,
            "facing": "Right",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 11
                },
                {
                    "x": 1,
                    "y": 11
                },
                {
                    "x": 0,
                    "y": 11
                }
            ]
        },
        {
            "id": 4,
            "facing": "Up",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 14
                },
                {
                    "x": 8,
                    "y": 15
                },
                {
                    "x": 8,
                    "y": 16
                }
            ]
        },
        {
            "id": 5,
            "facing": "Up",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 4
                },
                {
                    "x": 11,
                    "y": 5
                },
                {
                    "x": 11,
                    "y": 6
                }
            ]
        },
        {
            "id": 6,
            "facing": "Up",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 7
                },
                {
                    "x": 11,
                    "y": 8
                },
                {
                    "x": 11,
                    "y": 9
                }
            ]
        },
        {
            "id": 7,
            "facing": "Down",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 13
                },
                {
                    "x": 11,
                    "y": 12
                },
                {
                    "x": 11,
                    "y": 11
                }
            ]
        },
        {
            "id": 8,
            "facing": "Down",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 16
                },
                {
                    "x": 11,
                    "y": 15
                },
                {
                    "x": 11,
                    "y": 14
                }
            ]
        },
        {
            "id": 9,
            "facing": "Down",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 4
                },
                {
                    "x": 0,
                    "y": 3
                },
                {
                    "x": 0,
                    "y": 2
                }
            ]
        },
        {
            "id": 10,
            "facing": "Left",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 7
                },
                {
                    "x": 1,
                    "y": 7
                },
                {
                    "x": 2,
                    "y": 7
                }
            ]
        },
        {
            "id": 11,
            "facing": "Down",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 15
                },
                {
                    "x": 0,
                    "y": 14
                },
                {
                    "x": 0,
                    "y": 13
                }
            ]
        },
        {
            "id": 12,
            "facing": "Right",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 13
                },
                {
                    "x": 4,
                    "y": 13
                },
                {
                    "x": 3,
                    "y": 13
                }
            ]
        },
        {
            "id": 13,
            "facing": "Down",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 10
                },
                {
                    "x": 6,
                    "y": 9
                },
                {
                    "x": 6,
                    "y": 8
                }
            ]
        },
        {
            "id": 14,
            "facing": "Left",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 9
                },
                {
                    "x": 4,
                    "y": 9
                },
                {
                    "x": 5,
                    "y": 9
                }
            ]
        },
        {
            "id": 15,
            "facing": "Left",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 3
                },
                {
                    "x": 2,
                    "y": 3
                },
                {
                    "x": 3,
                    "y": 3
                }
            ]
        },
        {
            "id": 16,
            "facing": "Up",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 1
                },
                {
                    "x": 6,
                    "y": 2
                },
                {
                    "x": 6,
                    "y": 3
                }
            ]
        },
        {
            "id": 17,
            "facing": "Left",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 17
                },
                {
                    "x": 9,
                    "y": 17
                },
                {
                    "x": 10,
                    "y": 17
                }
            ]
        },
        {
            "id": 18,
            "facing": "Left",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 17
                },
                {
                    "x": 2,
                    "y": 17
                },
                {
                    "x": 3,
                    "y": 17
                }
            ]
        },
        {
            "id": 19,
            "facing": "Up",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 3
                },
                {
                    "x": 9,
                    "y": 4
                },
                {
                    "x": 9,
                    "y": 5
                }
            ]
        },
        {
            "id": 20,
            "facing": "Right",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 11
                },
                {
                    "x": 6,
                    "y": 11
                },
                {
                    "x": 5,
                    "y": 11
                }
            ]
        },
        {
            "id": 21,
            "facing": "Right",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 2
                },
                {
                    "x": 10,
                    "y": 2
                },
                {
                    "x": 9,
                    "y": 2
                }
            ]
        },
        {
            "id": 22,
            "facing": "Right",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 16
                },
                {
                    "x": 1,
                    "y": 16
                },
                {
                    "x": 0,
                    "y": 16
                }
            ]
        },
        {
            "id": 23,
            "facing": "Right",
            "pigType": "Long",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 15
                },
                {
                    "x": 6,
                    "y": 15
                },
                {
                    "x": 5,
                    "y": 15
                }
            ]
        },
        {
            "id": 24,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 17
                },
                {
                    "x": 5,
                    "y": 16
                }
            ]
        },
        {
            "id": 25,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 17
                },
                {
                    "x": 4,
                    "y": 16
                }
            ]
        },
        {
            "id": 26,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 15
                },
                {
                    "x": 3,
                    "y": 14
                }
            ]
        },
        {
            "id": 27,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 16
                },
                {
                    "x": 6,
                    "y": 16
                }
            ]
        },
        {
            "id": 28,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 17
                },
                {
                    "x": 7,
                    "y": 17
                }
            ]
        },
        {
            "id": 29,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 15
                },
                {
                    "x": 9,
                    "y": 15
                }
            ]
        },
        {
            "id": 30,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 16
                },
                {
                    "x": 9,
                    "y": 16
                }
            ]
        },
        {
            "id": 31,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 12
                },
                {
                    "x": 9,
                    "y": 12
                }
            ]
        },
        {
            "id": 32,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 13
                },
                {
                    "x": 9,
                    "y": 13
                }
            ]
        },
        {
            "id": 33,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 14
                },
                {
                    "x": 10,
                    "y": 14
                }
            ]
        },
        {
            "id": 34,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 12
                },
                {
                    "x": 8,
                    "y": 13
                }
            ]
        },
        {
            "id": 35,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 14
                },
                {
                    "x": 7,
                    "y": 13
                }
            ]
        },
        {
            "id": 36,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 15
                },
                {
                    "x": 1,
                    "y": 15
                }
            ]
        },
        {
            "id": 37,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 14
                },
                {
                    "x": 2,
                    "y": 14
                }
            ]
        },
        {
            "id": 38,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 13
                },
                {
                    "x": 2,
                    "y": 13
                }
            ]
        },
        {
            "id": 39,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 12
                },
                {
                    "x": 0,
                    "y": 12
                }
            ]
        },
        {
            "id": 40,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 12
                },
                {
                    "x": 2,
                    "y": 12
                }
            ]
        },
        {
            "id": 41,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 12
                },
                {
                    "x": 4,
                    "y": 11
                }
            ]
        },
        {
            "id": 42,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 11
                },
                {
                    "x": 3,
                    "y": 10
                }
            ]
        },
        {
            "id": 43,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 10
                },
                {
                    "x": 2,
                    "y": 10
                }
            ]
        },
        {
            "id": 44,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 10
                },
                {
                    "x": 0,
                    "y": 9
                }
            ]
        },
        {
            "id": 45,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 8
                },
                {
                    "x": 2,
                    "y": 9
                }
            ]
        },
        {
            "id": 46,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 8
                },
                {
                    "x": 0,
                    "y": 8
                }
            ]
        },
        {
            "id": 47,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 11
                },
                {
                    "x": 9,
                    "y": 10
                }
            ]
        },
        {
            "id": 48,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 10
                },
                {
                    "x": 7,
                    "y": 9
                }
            ]
        },
        {
            "id": 49,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 10
                },
                {
                    "x": 10,
                    "y": 10
                }
            ]
        },
        {
            "id": 50,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 9
                },
                {
                    "x": 9,
                    "y": 9
                }
            ]
        },
        {
            "id": 51,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 9
                },
                {
                    "x": 10,
                    "y": 8
                }
            ]
        },
        {
            "id": 52,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 7
                },
                {
                    "x": 10,
                    "y": 7
                }
            ]
        },
        {
            "id": 53,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 7
                },
                {
                    "x": 7,
                    "y": 7
                }
            ]
        },
        {
            "id": 54,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 6
                },
                {
                    "x": 1,
                    "y": 6
                }
            ]
        },
        {
            "id": 55,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 6
                },
                {
                    "x": 2,
                    "y": 6
                }
            ]
        },
        {
            "id": 56,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 6
                },
                {
                    "x": 5,
                    "y": 6
                }
            ]
        },
        {
            "id": 57,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 6
                },
                {
                    "x": 4,
                    "y": 7
                }
            ]
        },
        {
            "id": 58,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 8
                },
                {
                    "x": 5,
                    "y": 7
                }
            ]
        },
        {
            "id": 59,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 8
                },
                {
                    "x": 3,
                    "y": 8
                }
            ]
        },
        {
            "id": 60,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 6
                },
                {
                    "x": 8,
                    "y": 7
                }
            ]
        },
        {
            "id": 61,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 5
                },
                {
                    "x": 6,
                    "y": 4
                }
            ]
        },
        {
            "id": 62,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 6
                },
                {
                    "x": 7,
                    "y": 5
                }
            ]
        },
        {
            "id": 63,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 6
                },
                {
                    "x": 10,
                    "y": 5
                }
            ]
        },
        {
            "id": 64,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 4
                },
                {
                    "x": 7,
                    "y": 4
                }
            ]
        },
        {
            "id": 65,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 2
                },
                {
                    "x": 8,
                    "y": 3
                }
            ]
        },
        {
            "id": 66,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 3
                },
                {
                    "x": 7,
                    "y": 2
                }
            ]
        },
        {
            "id": 67,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 3
                },
                {
                    "x": 11,
                    "y": 3
                }
            ]
        },
        {
            "id": 68,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 1
                },
                {
                    "x": 10,
                    "y": 1
                }
            ]
        },
        {
            "id": 69,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 1
                },
                {
                    "x": 8,
                    "y": 1
                }
            ]
        },
        {
            "id": 70,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 1
                },
                {
                    "x": 5,
                    "y": 1
                }
            ]
        },
        {
            "id": 71,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 2
                },
                {
                    "x": 4,
                    "y": 2
                }
            ]
        },
        {
            "id": 72,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 4
                },
                {
                    "x": 1,
                    "y": 5
                }
            ]
        },
        {
            "id": 73,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 4
                },
                {
                    "x": 2,
                    "y": 5
                }
            ]
        },
        {
            "id": 74,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 1
                },
                {
                    "x": 2,
                    "y": 2
                }
            ]
        },
        {
            "id": 75,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 1
                },
                {
                    "x": 1,
                    "y": 2
                }
            ]
        },
        {
            "id": 76,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 2
                },
                {
                    "x": 3,
                    "y": 1
                }
            ]
        },
        {
            "id": 77,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 4
                },
                {
                    "x": 3,
                    "y": 4
                }
            ]
        },
        {
            "id": 78,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 5
                },
                {
                    "x": 4,
                    "y": 5
                }
            ]
        }
    ]
}
""";

    public const string Level59 = """
{
    "levelId": 59,
    "difficulty": 3,
    "levelName": "nlevel 63",
    "mapWidth": 12,
    "mapHeight": 19,
    "pigCount": 96,
    "pigs": [
        {
            "id": 0,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 11
                },
                {
                    "x": 7,
                    "y": 11
                }
            ]
        },
        {
            "id": 1,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 1
                },
                {
                    "x": 7,
                    "y": 1
                }
            ]
        },
        {
            "id": 2,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 11
                },
                {
                    "x": 5,
                    "y": 10
                }
            ]
        },
        {
            "id": 3,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 11
                },
                {
                    "x": 2,
                    "y": 10
                }
            ]
        },
        {
            "id": 4,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 3
                },
                {
                    "x": 10,
                    "y": 4
                }
            ]
        },
        {
            "id": 5,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 11
                },
                {
                    "x": 9,
                    "y": 11
                }
            ]
        },
        {
            "id": 6,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 7
                },
                {
                    "x": 6,
                    "y": 8
                }
            ]
        },
        {
            "id": 7,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 1
                },
                {
                    "x": 9,
                    "y": 2
                }
            ]
        },
        {
            "id": 8,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 2
                },
                {
                    "x": 10,
                    "y": 2
                }
            ]
        },
        {
            "id": 9,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 13
                },
                {
                    "x": 9,
                    "y": 13
                }
            ]
        },
        {
            "id": 10,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 5
                },
                {
                    "x": 10,
                    "y": 6
                }
            ]
        },
        {
            "id": 11,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 13
                },
                {
                    "x": 10,
                    "y": 12
                }
            ]
        },
        {
            "id": 12,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 11
                },
                {
                    "x": 8,
                    "y": 10
                }
            ]
        },
        {
            "id": 13,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 12
                },
                {
                    "x": 8,
                    "y": 12
                }
            ]
        },
        {
            "id": 14,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 10
                },
                {
                    "x": 9,
                    "y": 10
                }
            ]
        },
        {
            "id": 15,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 4
                },
                {
                    "x": 8,
                    "y": 4
                }
            ]
        },
        {
            "id": 16,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 8
                },
                {
                    "x": 5,
                    "y": 7
                }
            ]
        },
        {
            "id": 17,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 1
                },
                {
                    "x": 5,
                    "y": 1
                }
            ]
        },
        {
            "id": 18,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 7
                },
                {
                    "x": 11,
                    "y": 7
                }
            ]
        },
        {
            "id": 19,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 7
                },
                {
                    "x": 8,
                    "y": 7
                }
            ]
        },
        {
            "id": 20,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 9
                },
                {
                    "x": 7,
                    "y": 8
                }
            ]
        },
        {
            "id": 21,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 6
                },
                {
                    "x": 7,
                    "y": 5
                }
            ]
        },
        {
            "id": 22,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 9
                },
                {
                    "x": 11,
                    "y": 8
                }
            ]
        },
        {
            "id": 23,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 5
                },
                {
                    "x": 9,
                    "y": 5
                }
            ]
        },
        {
            "id": 24,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 4
                },
                {
                    "x": 11,
                    "y": 3
                }
            ]
        },
        {
            "id": 25,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 8
                },
                {
                    "x": 10,
                    "y": 9
                }
            ]
        },
        {
            "id": 26,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 15
                },
                {
                    "x": 10,
                    "y": 15
                }
            ]
        },
        {
            "id": 27,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 8
                },
                {
                    "x": 8,
                    "y": 9
                }
            ]
        },
        {
            "id": 28,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 9
                },
                {
                    "x": 9,
                    "y": 8
                }
            ]
        },
        {
            "id": 29,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 6
                },
                {
                    "x": 9,
                    "y": 7
                }
            ]
        },
        {
            "id": 30,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 3
                },
                {
                    "x": 7,
                    "y": 4
                }
            ]
        },
        {
            "id": 31,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 2
                },
                {
                    "x": 8,
                    "y": 2
                }
            ]
        },
        {
            "id": 32,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 3
                },
                {
                    "x": 6,
                    "y": 4
                }
            ]
        },
        {
            "id": 33,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 2
                },
                {
                    "x": 6,
                    "y": 2
                }
            ]
        },
        {
            "id": 34,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 3
                },
                {
                    "x": 4,
                    "y": 3
                }
            ]
        },
        {
            "id": 35,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 2
                },
                {
                    "x": 3,
                    "y": 3
                }
            ]
        },
        {
            "id": 36,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 4
                },
                {
                    "x": 5,
                    "y": 4
                }
            ]
        },
        {
            "id": 37,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 5
                },
                {
                    "x": 2,
                    "y": 5
                }
            ]
        },
        {
            "id": 38,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 5
                },
                {
                    "x": 3,
                    "y": 6
                }
            ]
        },
        {
            "id": 39,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 5
                },
                {
                    "x": 4,
                    "y": 6
                }
            ]
        },
        {
            "id": 40,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 7
                },
                {
                    "x": 4,
                    "y": 7
                }
            ]
        },
        {
            "id": 41,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 8
                },
                {
                    "x": 4,
                    "y": 8
                }
            ]
        },
        {
            "id": 42,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 6
                },
                {
                    "x": 1,
                    "y": 6
                }
            ]
        },
        {
            "id": 43,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 7
                },
                {
                    "x": 2,
                    "y": 8
                }
            ]
        },
        {
            "id": 44,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 7
                },
                {
                    "x": 1,
                    "y": 8
                }
            ]
        },
        {
            "id": 45,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 9
                },
                {
                    "x": 2,
                    "y": 9
                }
            ]
        },
        {
            "id": 46,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 9
                },
                {
                    "x": 3,
                    "y": 9
                }
            ]
        },
        {
            "id": 47,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 9
                },
                {
                    "x": 5,
                    "y": 9
                }
            ]
        },
        {
            "id": 48,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 11
                },
                {
                    "x": 3,
                    "y": 10
                }
            ]
        },
        {
            "id": 49,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 10
                },
                {
                    "x": 4,
                    "y": 11
                }
            ]
        },
        {
            "id": 50,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 13
                },
                {
                    "x": 5,
                    "y": 12
                }
            ]
        },
        {
            "id": 51,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 13
                },
                {
                    "x": 7,
                    "y": 13
                }
            ]
        },
        {
            "id": 52,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 8,
                    "y": 15
                },
                {
                    "x": 7,
                    "y": 15
                }
            ]
        },
        {
            "id": 53,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 15
                },
                {
                    "x": 9,
                    "y": 14
                }
            ]
        },
        {
            "id": 54,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 10
                },
                {
                    "x": 6,
                    "y": 10
                }
            ]
        },
        {
            "id": 55,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 17
                },
                {
                    "x": 7,
                    "y": 16
                }
            ]
        },
        {
            "id": 56,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 16
                },
                {
                    "x": 6,
                    "y": 17
                }
            ]
        },
        {
            "id": 57,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 7,
                    "y": 14
                },
                {
                    "x": 8,
                    "y": 14
                }
            ]
        },
        {
            "id": 58,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 12
                },
                {
                    "x": 7,
                    "y": 12
                }
            ]
        },
        {
            "id": 59,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 13
                },
                {
                    "x": 4,
                    "y": 13
                }
            ]
        },
        {
            "id": 60,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 12
                },
                {
                    "x": 4,
                    "y": 12
                }
            ]
        },
        {
            "id": 61,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 13
                },
                {
                    "x": 1,
                    "y": 12
                }
            ]
        },
        {
            "id": 62,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 11
                },
                {
                    "x": 1,
                    "y": 11
                }
            ]
        },
        {
            "id": 63,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 10
                },
                {
                    "x": 0,
                    "y": 10
                }
            ]
        },
        {
            "id": 64,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 5
                },
                {
                    "x": 5,
                    "y": 6
                }
            ]
        },
        {
            "id": 65,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 8
                },
                {
                    "x": 0,
                    "y": 9
                }
            ]
        },
        {
            "id": 66,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 5
                },
                {
                    "x": 0,
                    "y": 6
                }
            ]
        },
        {
            "id": 67,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 6
                },
                {
                    "x": 11,
                    "y": 5
                }
            ]
        },
        {
            "id": 68,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 11
                },
                {
                    "x": 11,
                    "y": 10
                }
            ]
        },
        {
            "id": 69,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 13
                },
                {
                    "x": 11,
                    "y": 12
                }
            ]
        },
        {
            "id": 70,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 10,
                    "y": 14
                },
                {
                    "x": 11,
                    "y": 14
                }
            ]
        },
        {
            "id": 71,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 15
                },
                {
                    "x": 5,
                    "y": 15
                }
            ]
        },
        {
            "id": 72,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 15
                },
                {
                    "x": 4,
                    "y": 14
                }
            ]
        },
        {
            "id": 73,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 13
                },
                {
                    "x": 2,
                    "y": 12
                }
            ]
        },
        {
            "id": 74,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 12
                },
                {
                    "x": 0,
                    "y": 13
                }
            ]
        },
        {
            "id": 75,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 15
                },
                {
                    "x": 1,
                    "y": 14
                }
            ]
        },
        {
            "id": 76,
            "facing": "Down",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 15
                },
                {
                    "x": 0,
                    "y": 14
                }
            ]
        },
        {
            "id": 77,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 16
                },
                {
                    "x": 4,
                    "y": 16
                }
            ]
        },
        {
            "id": 78,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 15
                },
                {
                    "x": 2,
                    "y": 15
                }
            ]
        },
        {
            "id": 79,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 16
                },
                {
                    "x": 0,
                    "y": 16
                }
            ]
        },
        {
            "id": 80,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 11,
                    "y": 16
                },
                {
                    "x": 10,
                    "y": 16
                }
            ]
        },
        {
            "id": 81,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 16
                },
                {
                    "x": 8,
                    "y": 16
                }
            ]
        },
        {
            "id": 82,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 5,
                    "y": 17
                },
                {
                    "x": 4,
                    "y": 17
                }
            ]
        },
        {
            "id": 83,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 2
                },
                {
                    "x": 2,
                    "y": 3
                }
            ]
        },
        {
            "id": 84,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 4
                },
                {
                    "x": 3,
                    "y": 4
                }
            ]
        },
        {
            "id": 85,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 3
                },
                {
                    "x": 0,
                    "y": 4
                }
            ]
        },
        {
            "id": 86,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 1,
                    "y": 3
                },
                {
                    "x": 1,
                    "y": 4
                }
            ]
        },
        {
            "id": 87,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 1
                },
                {
                    "x": 2,
                    "y": 1
                }
            ]
        },
        {
            "id": 88,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 3
                },
                {
                    "x": 8,
                    "y": 3
                }
            ]
        },
        {
            "id": 89,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 4,
                    "y": 1
                },
                {
                    "x": 4,
                    "y": 2
                }
            ]
        },
        {
            "id": 90,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 0,
                    "y": 2
                },
                {
                    "x": 1,
                    "y": 2
                }
            ]
        },
        {
            "id": 91,
            "facing": "Left",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 2,
                    "y": 14
                },
                {
                    "x": 3,
                    "y": 14
                }
            ]
        },
        {
            "id": 92,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 16
                },
                {
                    "x": 2,
                    "y": 16
                }
            ]
        },
        {
            "id": 93,
            "facing": "Up",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 6,
                    "y": 5
                },
                {
                    "x": 6,
                    "y": 6
                }
            ]
        },
        {
            "id": 94,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 3,
                    "y": 17
                },
                {
                    "x": 2,
                    "y": 17
                }
            ]
        },
        {
            "id": 95,
            "facing": "Right",
            "pigType": "Normal",
            "specialInfo": "",
            "occupiedCells": [
                {
                    "x": 9,
                    "y": 17
                },
                {
                    "x": 8,
                    "y": 17
                }
            ]
        }
    ]
}
""";

    public const string Dense112 = """
{"levelId": 112, "difficulty": 3, "levelName": "dense-peel", "mapWidth": 12, "mapHeight": 19, "pigCount": 112, "pigs": [{"id": 0, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 0}, {"x": 1, "y": 0}]}, {"id": 1, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 0}, {"x": 3, "y": 0}]}, {"id": 2, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 1}, {"x": 1, "y": 1}]}, {"id": 3, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 1}, {"x": 3, "y": 1}]}, {"id": 4, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 2}, {"x": 1, "y": 2}]}, {"id": 5, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 2}, {"x": 3, "y": 2}]}, {"id": 6, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 3}, {"x": 1, "y": 3}]}, {"id": 7, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 3}, {"x": 3, "y": 3}]}, {"id": 8, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 4}, {"x": 1, "y": 4}]}, {"id": 9, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 4}, {"x": 3, "y": 4}]}, {"id": 10, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 5}, {"x": 1, "y": 5}]}, {"id": 11, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 5}, {"x": 3, "y": 5}]}, {"id": 12, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 6}, {"x": 1, "y": 6}]}, {"id": 13, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 6}, {"x": 3, "y": 6}]}, {"id": 14, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 7}, {"x": 1, "y": 7}]}, {"id": 15, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 7}, {"x": 3, "y": 7}]}, {"id": 16, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 8}, {"x": 1, "y": 8}]}, {"id": 17, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 8}, {"x": 3, "y": 8}]}, {"id": 18, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 9}, {"x": 1, "y": 9}]}, {"id": 19, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 9}, {"x": 3, "y": 9}]}, {"id": 20, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 10}, {"x": 1, "y": 10}]}, {"id": 21, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 10}, {"x": 3, "y": 10}]}, {"id": 22, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 11}, {"x": 1, "y": 11}]}, {"id": 23, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 11}, {"x": 3, "y": 11}]}, {"id": 24, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 12}, {"x": 1, "y": 12}]}, {"id": 25, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 12}, {"x": 3, "y": 12}]}, {"id": 26, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 13}, {"x": 1, "y": 13}]}, {"id": 27, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 13}, {"x": 3, "y": 13}]}, {"id": 28, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 14}, {"x": 1, "y": 14}]}, {"id": 29, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 14}, {"x": 3, "y": 14}]}, {"id": 30, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 15}, {"x": 1, "y": 15}]}, {"id": 31, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 15}, {"x": 3, "y": 15}]}, {"id": 32, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 16}, {"x": 1, "y": 16}]}, {"id": 33, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 16}, {"x": 3, "y": 16}]}, {"id": 34, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 17}, {"x": 1, "y": 17}]}, {"id": 35, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 17}, {"x": 3, "y": 17}]}, {"id": 36, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 0, "y": 18}, {"x": 1, "y": 18}]}, {"id": 37, "facing": "Left", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 2, "y": 18}, {"x": 3, "y": 18}]}, {"id": 38, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 0}, {"x": 9, "y": 0}]}, {"id": 39, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 0}, {"x": 11, "y": 0}]}, {"id": 40, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 1}, {"x": 9, "y": 1}]}, {"id": 41, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 1}, {"x": 11, "y": 1}]}, {"id": 42, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 2}, {"x": 9, "y": 2}]}, {"id": 43, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 2}, {"x": 11, "y": 2}]}, {"id": 44, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 3}, {"x": 9, "y": 3}]}, {"id": 45, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 3}, {"x": 11, "y": 3}]}, {"id": 46, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 4}, {"x": 9, "y": 4}]}, {"id": 47, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 4}, {"x": 11, "y": 4}]}, {"id": 48, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 5}, {"x": 9, "y": 5}]}, {"id": 49, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 5}, {"x": 11, "y": 5}]}, {"id": 50, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 6}, {"x": 9, "y": 6}]}, {"id": 51, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 6}, {"x": 11, "y": 6}]}, {"id": 52, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 7}, {"x": 9, "y": 7}]}, {"id": 53, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 7}, {"x": 11, "y": 7}]}, {"id": 54, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 8}, {"x": 9, "y": 8}]}, {"id": 55, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 8}, {"x": 11, "y": 8}]}, {"id": 56, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 9}, {"x": 9, "y": 9}]}, {"id": 57, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 9}, {"x": 11, "y": 9}]}, {"id": 58, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 10}, {"x": 9, "y": 10}]}, {"id": 59, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 10}, {"x": 11, "y": 10}]}, {"id": 60, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 11}, {"x": 9, "y": 11}]}, {"id": 61, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 11}, {"x": 11, "y": 11}]}, {"id": 62, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 12}, {"x": 9, "y": 12}]}, {"id": 63, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 12}, {"x": 11, "y": 12}]}, {"id": 64, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 13}, {"x": 9, "y": 13}]}, {"id": 65, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 13}, {"x": 11, "y": 13}]}, {"id": 66, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 14}, {"x": 9, "y": 14}]}, {"id": 67, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 14}, {"x": 11, "y": 14}]}, {"id": 68, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 15}, {"x": 9, "y": 15}]}, {"id": 69, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 15}, {"x": 11, "y": 15}]}, {"id": 70, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 16}, {"x": 9, "y": 16}]}, {"id": 71, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 16}, {"x": 11, "y": 16}]}, {"id": 72, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 17}, {"x": 9, "y": 17}]}, {"id": 73, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 17}, {"x": 11, "y": 17}]}, {"id": 74, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 8, "y": 18}, {"x": 9, "y": 18}]}, {"id": 75, "facing": "Right", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 10, "y": 18}, {"x": 11, "y": 18}]}, {"id": 76, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 4, "y": 0}, {"x": 4, "y": 1}]}, {"id": 77, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 4, "y": 2}, {"x": 4, "y": 3}]}, {"id": 78, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 4, "y": 4}, {"x": 4, "y": 5}]}, {"id": 79, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 4, "y": 6}, {"x": 4, "y": 7}]}, {"id": 80, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 4, "y": 8}, {"x": 4, "y": 9}]}, {"id": 81, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 4, "y": 10}, {"x": 4, "y": 11}]}, {"id": 82, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 4, "y": 12}, {"x": 4, "y": 13}]}, {"id": 83, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 4, "y": 14}, {"x": 4, "y": 15}]}, {"id": 84, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 4, "y": 16}, {"x": 4, "y": 17}]}, {"id": 85, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 5, "y": 0}, {"x": 5, "y": 1}]}, {"id": 86, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 5, "y": 2}, {"x": 5, "y": 3}]}, {"id": 87, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 5, "y": 4}, {"x": 5, "y": 5}]}, {"id": 88, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 5, "y": 6}, {"x": 5, "y": 7}]}, {"id": 89, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 5, "y": 8}, {"x": 5, "y": 9}]}, {"id": 90, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 5, "y": 10}, {"x": 5, "y": 11}]}, {"id": 91, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 5, "y": 12}, {"x": 5, "y": 13}]}, {"id": 92, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 5, "y": 14}, {"x": 5, "y": 15}]}, {"id": 93, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 5, "y": 16}, {"x": 5, "y": 17}]}, {"id": 94, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 6, "y": 0}, {"x": 6, "y": 1}]}, {"id": 95, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 6, "y": 2}, {"x": 6, "y": 3}]}, {"id": 96, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 6, "y": 4}, {"x": 6, "y": 5}]}, {"id": 97, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 6, "y": 6}, {"x": 6, "y": 7}]}, {"id": 98, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 6, "y": 8}, {"x": 6, "y": 9}]}, {"id": 99, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 6, "y": 10}, {"x": 6, "y": 11}]}, {"id": 100, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 6, "y": 12}, {"x": 6, "y": 13}]}, {"id": 101, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 6, "y": 14}, {"x": 6, "y": 15}]}, {"id": 102, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 6, "y": 16}, {"x": 6, "y": 17}]}, {"id": 103, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 7, "y": 0}, {"x": 7, "y": 1}]}, {"id": 104, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 7, "y": 2}, {"x": 7, "y": 3}]}, {"id": 105, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 7, "y": 4}, {"x": 7, "y": 5}]}, {"id": 106, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 7, "y": 6}, {"x": 7, "y": 7}]}, {"id": 107, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 7, "y": 8}, {"x": 7, "y": 9}]}, {"id": 108, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 7, "y": 10}, {"x": 7, "y": 11}]}, {"id": 109, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 7, "y": 12}, {"x": 7, "y": 13}]}, {"id": 110, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 7, "y": 14}, {"x": 7, "y": 15}]}, {"id": 111, "facing": "Up", "pigType": "Normal", "specialInfo": "", "occupiedCells": [{"x": 7, "y": 16}, {"x": 7, "y": 17}]}], "terrainElements": [], "specialData": {"rabbit": {"x": 0, "y": 0}, "pigSkin": ""}}
""";

    public static readonly string[] Band2 = [Level374];
    public static readonly string[] Band3 = [Level19, Level29, Level59, Dense112];
    public static readonly string[] Dense = [Dense112];
}
