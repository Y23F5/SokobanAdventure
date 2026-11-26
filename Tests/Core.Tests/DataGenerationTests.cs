/**
 * @file DataGenerationTests.cs
 * @brief 数据生成测试：用关卡图纸转换器重新生成正式房间、世界文件与默认房间，只有环境变量 SOKOBAN_GENERATE=1 时才真正写文件，否则直接通过。
 * GenerateData：转换仓库、工厂、森林图纸并设置名称与主题，加恢复道具；森林先用求解器验证 13×8 地图，确认有解或乐观有解才采用，否则保留 11×9 图纸；对三房跑发布级校验与求解，写出房间与世界文件、DefaultRoom.json，再复制到 Docs/关卡样例 并重新生成传送测试样例，最后逐个读回核对。
 * Build：图纸转房间草稿，按格子重命名门 ID，主入口取第一个 entry 门，恢复道具由 Items 给出。
 * Items：先用 RecoveryMapping.FromPresentation 取恢复道具；森林房内有可开合地形时，藤门参数以该格坐标与通道朝向替换。
 * FindOpenable、GateParams：定位第一个可开合地形格，藤门 gateX、gateZ 取该格坐标，yaw 取通道一侧朝向的角度，通道轴向由左右或上下两侧是否为地板判定。
 * Root、Put、SpawnOf：定位仓库根目录、以无 BOM 的 UTF-8 写文件、取房间出生点格子。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Sokoban.Core.Tests
{
    public class DataGenerationTests
    {
        const int Budget = 2000000;

        static readonly string[] Warehouse =
        {
            "#########",
            "#.......#",
            "#.......#",
            "<.......>",
            "#.P.B.T.#",
            "#.......#",
            "#########",
        };

        static readonly string[] Factory =
        {
            "###########",
            "#....######",
            "#....B.TT##",
            "<......##.>",
            "#...B.###.#",
            "#.P.......#",
            "###########",
        };

        static readonly string[] ForestNew =
        {
            "#############",
            "#...######T##",
            "#...V.B.....#",
            "<...#..##...#",
            "#.P.#..##...>",
            "#####..B....#",
            "##########T##",
            "#############",
        };

        static readonly string[] ForestOld =
        {
            "###########",
            "#..T#.....#",
            "#...#..T..#",
            "#.B...#...#",
            "#..##..B..#",
            "<...#..#..>",
            "#.P....#..#",
            "#.........#",
            "###########",
        };

        static readonly string[] PortalWarehouse =
        {
            "####>####",
            "#.......#",
            "#.......#",
            "<.......>",
            "#.P.B.T.#",
            "#.......#",
            "#########",
        };

        static readonly string[] PortalForest =
        {
            "###########",
            "#..T#.....#",
            "#...#..T..#",
            "#.B...#...#",
            "#..##..B..#",
            "<...#..#..>",
            "#.P....#..#",
            "#.........#",
            "##<########",
        };

        readonly ITestOutputHelper log;

        public DataGenerationTests(ITestOutputHelper log)
        {
            this.log = log;
        }

        static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Assets")) && Directory.Exists(Path.Combine(dir.FullName, "Tests"))))
                dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("找不到仓库根目录");
            return dir.FullName;
        }

        static void Put(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        static Cell SpawnOf(Room room) => room.ObjectsOf(ObjectCategories.Spawn).Single().Cell;

        static Cell? FindOpenable(Room room)
        {
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                    if (room.TerrainAt(new Cell(x, y)).CategoryId == TerrainCategories.Openable) return new Cell(x, y);
            return null;
        }

        static Dictionary<string, float> GateParams(Room room, Cell gate)
        {
            bool Open(Cell c) => room.TerrainAt(c).CategoryId == TerrainCategories.Floor;
            Cell inward;
            if (Open(gate + Cell.Left) && Open(gate + Cell.Right)) inward = Cell.Left;
            else if (Open(gate + Cell.Up) && Open(gate + Cell.Down)) inward = Cell.Down;
            else throw new InvalidOperationException("可开合地形格 " + gate + " 两侧都不是地板");
            return new Dictionary<string, float>(StringComparer.Ordinal)
            {
                ["gateX"] = gate.X,
                ["gateZ"] = gate.Y,
                ["yaw"] = (float)(Math.Atan2(inward.X, inward.Y) * 180.0 / Math.PI),
            };
        }

        static List<RecoveryItem> Items(string preset, RoomDraftModel bare)
        {
            var items = RecoveryMapping.FromPresentation(preset, bare).ToList();
            var gate = FindOpenable(bare.Room);
            if (preset == "forest" && gate.HasValue)
            {
                int index = items.FindIndex(x => x.ScriptRef == "forest-vine");
                string id = index >= 0 ? items[index].Id : "recovery-" + (items.Count + 1);
                var vine = new RecoveryItem(id, "forest-vine", RecoveryItem.DefaultDuration, GateParams(bare.Room, gate.Value));
                if (index >= 0) items[index] = vine; else items.Add(vine);
            }
            return items;
        }

        static RoomDraftModel Build(string id, string name, string[] rows, Dictionary<Cell, string> doorIds = null)
        {
            var parsed = Blueprint.Parse(id, rows);
            var doors = new List<DoorInfo>();
            foreach (var d in parsed.Doors)
            {
                string doorId = d.Id;
                if (doorIds != null && doorIds.TryGetValue(d.Cell, out var renamed)) doorId = renamed;
                if (doorId != d.Id) parsed.Room.SetTerrain(d.Cell, TerrainCategories.Door, doorId);
                doors.Add(new DoorInfo(doorId, d.Cell, d.Outward, d.Role));
            }
            string primary = doors.First(d => d.Role == "entry").Id;
            var bare = new RoomDraftModel(parsed.Room, doors, primary);
            var draft = new RoomDraftModel(parsed.Room, doors, primary, null, Items(id, bare));
            draft.Meta = new RoomMeta(name, id);
            return draft;
        }

        static RoomDraftModel Ws01()
        {
            var rows = new[] { "#######", "#.....#", "#.....#", "<.....>", "#.PBT.#", "#.....#", "#######" };
            var parsed = Blueprint.Parse("ws01-room", rows);
            parsed.Room.RemoveObject("box-1");
            parsed.Room.RemoveObject("target-1");
            parsed.Room.AddObject("box-a", ObjectCategories.Box, new Cell(3, 2));
            parsed.Room.AddObject("target-a", ObjectCategories.Target, new Cell(4, 2));
            var items = new List<RecoveryItem> { new RecoveryItem("recovery-1", "warehouse-lift", RecoveryItem.DefaultDuration) };
            var draft = new RoomDraftModel(parsed.Room, parsed.Doors, "entry", null, items);
            draft.Meta = new RoomMeta("双端交换测试房", "warehouse");
            return draft;
        }

        static RoomDraftModel Blank()
        {
            var rows = new[] { "#######", "#.....#", "#.....#", "<.....>", "#.P...#", "#.....#", "#######" };
            var parsed = Blueprint.Parse("blank", rows);
            var draft = new RoomDraftModel(parsed.Room, parsed.Doors, "entry");
            draft.Meta = new RoomMeta("新房间", "warehouse");
            return draft;
        }

        static WorldDocument World(string file1, string file2, string file3, Cell origin2, Cell origin3, string mode, bool shortcut)
        {
            var rooms = new List<WorldRoomRef>
            {
                new WorldRoomRef("warehouse", file1, new Cell(0, 1), new string[0]),
                new WorldRoomRef("factory", file2, origin2, new[] { "warehouse" }),
                new WorldRoomRef("forest", file3, origin3, new[] { "warehouse", "factory" }),
            };
            var links = new List<WorldConnection>
            {
                new WorldConnection("link-1", mode, new WorldEndpoint("warehouse", "exit"), new WorldEndpoint("factory", "entry"), true),
                new WorldConnection("link-2", mode, new WorldEndpoint("factory", "exit"), new WorldEndpoint("forest", "entry"), true),
            };
            if (shortcut) links.Add(new WorldConnection("shortcut", mode, new WorldEndpoint("warehouse", "side-exit"), new WorldEndpoint("forest", "side-entry"), true));
            return new WorldDocument(shortcut ? "portal-check" : "main", new Cell(0, 0), new Cell(63, 31), rooms, links,
                new WorldEndpoint("warehouse", "spawn"), new WorldEndpoint("forest", "exit"), new[] { "warehouse", "factory", "forest" });
        }

        [Fact]
        public void GenerateData()
        {
            if (Environment.GetEnvironmentVariable("SOKOBAN_GENERATE") != "1") return;
            string root = Root();
            string worldDir = Path.Combine(root, "Assets", "StreamingAssets", "World");
            string docsDir = Path.Combine(root, "Docs", "关卡样例");
            var written = new List<(string name, string text, RoomDraftModel source)>();
            void Emit(string name, string text, bool toWorld, RoomDraftModel source)
            {
                if (toWorld) Put(Path.Combine(worldDir, name), text);
                Put(Path.Combine(docsDir, name), text);
                written.Add((name, text, source));
            }

            var forestNew = Build("forest", "森林", ForestNew);
            var verdict = Solver.Judge(forestNew.Room, SpawnOf(forestNew.Room), Budget);
            log.WriteLine("森林 13x8 Solver：" + verdict.Verdict + "，展开状态 " + verdict.Expanded + "，步数 " + verdict.Moves.Count);
            bool useNew = verdict.Verdict == SolveVerdict.Confirmed || verdict.Verdict == SolveVerdict.Optimistic;
            var forest = useNew ? forestNew : Build("forest", "森林", ForestOld);
            log.WriteLine("森林采用：" + (useNew ? "13x8" : "11x9 旧图纸"));

            var rooms = new[]
            {
                ("warehouse", Build("warehouse", "仓库", Warehouse)),
                ("factory", Build("factory", "工厂", Factory)),
                ("forest", forest),
            };
            foreach (var (id, draft) in rooms)
            {
                var judged = Solver.Judge(draft.Room, SpawnOf(draft.Room), Budget);
                log.WriteLine(id + " Solver：" + judged.Verdict + "，展开状态 " + judged.Expanded + "，步数 " + judged.Moves.Count);
                var issues = RoomValidator.ValidateDraft(draft, ValidationStage.Publish);
                foreach (var i in issues) log.WriteLine(id + " 发布级 " + i.Severity + " " + i.RuleId + " @ " + i.Location + "：" + i.Message);
                Assert.False(RoomValidator.HasErrors(issues), id + " 发布级校验有错误");
                Emit(id + ".room.json", RoomCodec.Encode(draft), true, draft);
            }

            Emit("main.world.json", WorldCodec.Encode(World("warehouse.room.json", "factory.room.json", "forest.room.json", new Cell(9, 1), new Cell(20, 0), WorldCodec.Direct, false)), true, null);
            Put(Path.Combine(root, "Assets", "Sokoban", "Resources", "DefaultRoom.json"), RoomCodec.Encode(Blank()));

            var ws01 = Ws01();
            Emit("WS-01.json", RoomCodec.Encode(ws01), false, ws01);
            var pw = Build("warehouse", "仓库", PortalWarehouse, new Dictionary<Cell, string> { [new Cell(4, 6)] = "side-exit", [new Cell(8, 3)] = "exit" });
            var pf = Build("forest", "森林", PortalForest, new Dictionary<Cell, string> { [new Cell(2, 0)] = "side-entry" });
            Emit("portal-warehouse.room.json", RoomCodec.Encode(pw), false, pw);
            Emit("portal-forest.room.json", RoomCodec.Encode(pf), false, pf);
            Emit("portal-check.world.json", WorldCodec.Encode(World("portal-warehouse.room.json", "factory.room.json", "portal-forest.room.json", new Cell(40, 10), new Cell(20, 0), WorldCodec.Teleport, true)), false, null);

            foreach (var (name, text, source) in written)
            {
                if (name.EndsWith(".world.json"))
                {
                    var back = WorldCodec.Decode(text);
                    log.WriteLine(name + " 读回：房间 " + back.Rooms.Count + "，连接 " + back.Connections.Count);
                    Assert.Equal(3, back.Rooms.Count);
                    continue;
                }
                var read = RoomCodec.Decode(text);
                string hash = ContentHash.Of(read);
                Assert.Equal(ContentHash.Of(source), hash);
                Assert.Equal(text, RoomCodec.Encode(read));
                log.WriteLine(name + " 哈希 " + hash);
            }
            foreach (var name in new[] { "warehouse.room.json", "factory.room.json", "forest.room.json", "main.world.json" })
                Assert.Equal(File.ReadAllText(Path.Combine(worldDir, name)), File.ReadAllText(Path.Combine(docsDir, name)));
            RoomCodec.Decode(File.ReadAllText(Path.Combine(root, "Assets", "Sokoban", "Resources", "DefaultRoom.json")));
        }
    }
}
