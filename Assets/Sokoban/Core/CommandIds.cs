/**
 * @file CommandIds.cs
 * @brief 编辑命令 ID 注册表：命令 ID 采用 域.动作 命名，两端 UI 只绑定 ID，未知 ID 明确报错。
 * CommandInfo：命令 ID、所属域与是否改变模型（工具切换、网格显隐只改界面状态）。
 * CommandIds：全部命令 ID 常量。
 * CommandIds.CreateDefault()：登记全部命令，返回注册表。
 * CommandIds.Domain(id)：取 ID 中第一个点之前的域名。
 */
namespace Sokoban.Core
{
    public sealed class CommandInfo
    {
        public string Id { get; }
        public string Domain { get; }
        public bool ChangesModel { get; }

        public CommandInfo(string id, bool changesModel)
        {
            Id = id;
            Domain = CommandIds.Domain(id);
            ChangesModel = changesModel;
        }
    }

    public static class CommandIds
    {
        public const string FileNew = "file.new";
        public const string FileOpen = "file.open";
        public const string FileSave = "file.save";
        public const string FileImport = "file.import";
        public const string FileExport = "file.export";
        public const string EditUndo = "edit.undo";
        public const string EditRedo = "edit.redo";
        public const string ToolSelect = "tool.select";
        public const string ToolMove = "tool.move";
        public const string ToolErase = "tool.erase";
        public const string GridToggle = "grid.toggle";
        public const string TerrainPaint = "terrain.paint";
        public const string TerrainErase = "terrain.erase";
        public const string TerrainExpand = "terrain.expand";
        public const string ObjectPlace = "object.place";
        public const string ObjectMove = "object.move";
        public const string ObjectRemove = "object.remove";
        public const string ObjectSetAppearance = "object.setAppearance";
        public const string ObjectSetState = "object.setState";
        public const string ObjectInstantiateTemplate = "object.instantiateTemplate";
        public const string DoorSetDirection = "door.setDirection";
        public const string DoorSetRole = "door.setRole";
        public const string RecoveryAddItem = "recovery.addItem";
        public const string RecoveryRemoveItem = "recovery.removeItem";
        public const string RecoverySetScript = "recovery.setScript";
        public const string RecoverySetDuration = "recovery.setDuration";
        public const string RecoverySetParams = "recovery.setParams";
        public const string RoomSetSpawn = "room.setSpawn";
        public const string RoomSetPrimaryEntrance = "room.setPrimaryEntrance";
        public const string RoomEnterStandaloneEdit = "room.enterStandaloneEdit";
        public const string RoomReturnToWorld = "room.returnToWorld";
        public const string WorldPlaceRoom = "world.placeRoom";
        public const string WorldMoveRoom = "world.moveRoom";
        public const string WorldRemoveRoom = "world.removeRoom";
        public const string WorldConnectDoors = "world.connectDoors";
        public const string WorldPublish = "world.publish";
        public const string CheckStructure = "check.structure";
        public const string CheckSolvable = "check.solvable";
        public const string CheckLocate = "check.locate";
        public const string TrialStart = "trial.start";
        public const string TrialStop = "trial.stop";
        public const string PlayCycleItem = "play.cycleItem";
        public const string PlayUseItem = "play.useItem";
        public const string PlayAcceptItemNotice = "play.acceptItemNotice";

        public static string Domain(string id)
        {
            int dot = id.IndexOf('.');
            return dot < 0 ? id : id.Substring(0, dot);
        }

        static readonly string[] Passive =
        {
            FileNew, FileOpen, FileSave, FileImport, FileExport, EditUndo, EditRedo,
            ToolSelect, ToolMove, ToolErase, GridToggle, WorldPublish,
            CheckStructure, CheckSolvable, CheckLocate, TrialStart, TrialStop,
            RoomEnterStandaloneEdit, RoomReturnToWorld,
            PlayCycleItem, PlayUseItem, PlayAcceptItemNotice,
        };

        static readonly string[] Mutating =
        {
            TerrainPaint, TerrainErase, TerrainExpand,
            ObjectPlace, ObjectMove, ObjectRemove, ObjectSetAppearance, ObjectSetState, ObjectInstantiateTemplate,
            DoorSetDirection, DoorSetRole,
            RecoveryAddItem, RecoveryRemoveItem, RecoverySetScript, RecoverySetDuration, RecoverySetParams,
            RoomSetSpawn, RoomSetPrimaryEntrance,
            WorldPlaceRoom, WorldMoveRoom, WorldRemoveRoom, WorldConnectDoors,
        };

        public static Registry<CommandInfo> CreateDefault()
        {
            var registry = new Registry<CommandInfo>("command");
            foreach (var id in Passive) registry.Register(id, new CommandInfo(id, false));
            foreach (var id in Mutating) registry.Register(id, new CommandInfo(id, true));
            return registry;
        }
    }
}
