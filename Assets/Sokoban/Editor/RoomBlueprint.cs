/**
 * @file RoomBlueprint.cs
 * @brief Read 从北向南读取图纸，先通过共享笔刷铺地、砌墙，再设置端点和实体。
 * Import 通过文件选择器进入房间编辑；生成失败时保留当前工作区。
 */
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class RoomBlueprint
    {
        public static RoomDraft Read(string path)
        {
            var lines = File.ReadAllLines(path);
            RoomFile.Check(lines.Length >= 2, "图纸缺少房间信息或格子");
            var header = lines[0].Split('|');
            RoomFile.Check(header.Length == 3 && header.All(value => !string.IsNullOrWhiteSpace(value)), "首行应为房间 ID|名称|主题 ID");
            var size = new Vector2Int(lines[1].Length, lines.Length - 1);
            var tiles = new List<(Vector2Int cell, char symbol)>();
            foreach (int row in Enumerable.Range(0, size.y))
            {
                string line = lines[row + 1];
                RoomFile.Check(line.Length == size.x, "图纸行宽不一致");
                for (int x = 0; x < size.x; x++)
                {
                    RoomFile.Check("#.PBT<>_".Contains(line[x]), "图纸符号无法识别 " + line[x]);
                    tiles.Add((new Vector2Int(x, size.y - 1 - row), line[x]));
                }
            }
            RoomFile.Check(tiles.Count(tile => tile.symbol == 'P') == 1, "图纸需要一个出生点");
            var draft = new RoomDraft(Resources.Load<TextAsset>("DefaultRoom").text);
            draft.NewBlankRoom(header[0], header[1], header[2], size);
            draft.Brush(tiles.Where(t => t.symbol != '#' && t.symbol != '_').Select(t => t.cell), RoomTool.Floor);
            draft.Brush(tiles.Where(t => t.symbol == '#').Select(t => t.cell), RoomTool.Wall);
            foreach (var tile in tiles.Where(t => "<>P".Contains(t.symbol)))
                draft.SetRoomMarker(tile.cell, tile.symbol == 'P' ? "spawn" : tile.symbol == '<' ? "entry" : "exit");
            int boxes = 0, targets = 0;
            foreach (var tile in tiles.Where(t => t.symbol == 'B' || t.symbol == 'T'))
            {
                bool box = tile.symbol == 'B';
                draft.Place(box, tile.cell, box ? "box-" + ++boxes : "target-" + ++targets);
            }
            if (RoomFile.RecoveryPresets.Contains(header[2])) draft.SetRecoveryPreset(header[2]);
            RoomFile.Parse(draft.Room.ToString());
            return draft;
        }

        public static void Import(LayoutPanel panel)
        {
            var path = EditorUtility.OpenFilePanel("从格子图纸新建房间", Path.Combine(Application.dataPath,"Sokoban/Editor/Blueprints"), "txt");
            if (path.Length == 0) return;
            panel.Run(() => panel.OpenBlueprint(Read(path)), "已读取图纸");
        }
    }
}
