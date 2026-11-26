/**
 * @file PuzzleSave.cs
 * @brief 存档快照与读档重建：规则状态存房内坐标加对象 ID，演出状态单独存成演出段，规则层不读取它。
 * Snapshot()：生成 SaveData，含各房箱位、已解开记录、对象状态、探索集合、道具背包与演出段。
 * Restore(data)：在候选会话里完成全部校验再替换当前状态，字段无效抛 FormatException，由调用方归为损坏。
 * ApplySave(data)：逐房核验箱位、探索集合与背包，按演出段重建恢复进度、视角与进行中的移动或转向。
 * Presentation()：恢复进度 t 与时长、视角、移动种类与进度；转向的方向存在 MotionDelta 的第一个分量。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban
{
    public sealed partial class PuzzleRun
    {
        PresentationSegment Presentation()
        {
            var session = Current.Session;
            var segment = new PresentationSegment
            {
                Heading = Heading,
                RecoveryDuration = session.Recovering ? session.Duration : 0,
                RecoveryT = RecoveryFraction
            };
            if (Motion == null) return segment;
            segment.MotionKind = Motion.Kind;
            segment.MotionFraction = Motion.Fraction;
            segment.MotionDelta = Motion.Kind == "turn" ? new Cell(Motion.Direction, 0) : new Cell(Motion.Delta.x, Motion.Delta.y);
            segment.MotionPushed = Motion.Pushed != null;
            return segment;
        }

        public SaveData Snapshot()
        {
            var data = new SaveData
            {
                WorldId = WorldId,
                RoomId = ActiveId,
                PlayerX = local.X,
                PlayerY = local.Y,
                SelectedItem = bag.Selected,
                PendingItem = bag.Pending,
                Presentation = Presentation()
            };
            foreach (var pair in states)
            {
                data.Boxes[pair.Key] = pair.Value.Boxes.ToDictionary(b => b.Key, b => new[] { b.Value.X, b.Value.Y });
                data.EverSolved[pair.Key] = pair.Value.EverSolved;
                data.ObjectStates[pair.Key] = new Dictionary<string, string>(pair.Value.ObjectStates);
                data.Hashes[pair.Key] = Hashes[pair.Key];
            }
            data.Explored.UnionWith(exploration.Rooms);
            data.Items.AddRange(bag.Items);
            return data;
        }

        public void Restore(SaveData data)
        {
            if (!states.ContainsKey(data.RoomId)) throw new FormatException("存档房间不在世界中");
            var candidate = new PuzzleRun(World, Rooms, Models, Hashes, data.RoomId);
            candidate.ApplySave(data);
            states.Clear();
            foreach (var pair in candidate.states) states.Add(pair.Key, pair.Value);
            ActiveId = candidate.ActiveId;
            local = candidate.local;
            exploration = candidate.exploration;
            bag = candidate.bag;
            Heading = candidate.Heading;
            Motion = candidate.Motion;
        }

        void ApplySave(SaveData data)
        {
            var segment = data.Presentation ?? new PresentationSegment();
            foreach (var pair in states)
            {
                var progress = pair.Value;
                Check(data.Boxes.TryGetValue(pair.Key, out var saved) && saved.Count == progress.Boxes.Count
                    && saved.Keys.All(progress.Boxes.ContainsKey) && saved.Values.All(v => v != null && v.Length == 2), "箱子记录与房间不符：" + pair.Key);
                var cells = saved.ToDictionary(b => b.Key, b => new Cell(b.Value[0], b.Value[1]));
                Check(cells.Values.Distinct().Count() == cells.Count && cells.Values.All(progress.Session.IsWalkable), "箱子占格无效：" + pair.Key);
                foreach (var entry in cells) progress.Boxes[entry.Key] = entry.Value;
                data.EverSolved.TryGetValue(pair.Key, out bool ever);
                progress.EverSolved = ever || progress.Solved;
                progress.ObjectStates.Clear();
                if (data.ObjectStates.TryGetValue(pair.Key, out var objects))
                    foreach (var state in objects) progress.ObjectStates[state.Key] = state.Value;
            }
            Check(data.Explored.All(states.ContainsKey) && data.Explored.Contains(ActiveId), "探索集合无效");
            exploration = ExplorationSet.Restore(data.Explored);
            var known = states.Values.SelectMany(s => s.Validated.RecoveryItems.Select(i => i.Id)).ToList();
            Check(data.Items.All(known.Contains), "背包含未知道具");
            bag = ItemBag.Restore(data.Items, data.SelectedItem, data.PendingItem);
            Check(segment.Heading >= 0 && segment.Heading <= 3, "视角无效");
            Heading = (int)segment.Heading;
            var player = new Cell(data.PlayerX, data.PlayerY);
            local = player;
            var current = Current;
            if (segment.RecoveryDuration > 0)
            {
                var item = current.Validated.RecoveryItems.FirstOrDefault(i => Math.Abs(i.Duration - segment.RecoveryDuration) < 0.001f);
                Check(item != null && segment.RecoveryT >= 0 && segment.RecoveryT < 1, "恢复进度无效");
                var session = current.Rebuild(player);
                session.Start(item.Duration);
                Check(session.Player == player && session.IsWalkable(player) && !session.Boxes.ContainsValue(player) && !session.Zone.Contains(player), "恢复期间玩家位置无效");
                session.Advance((float)(item.Duration * segment.RecoveryT));
            }
            else Check(current.Session.IsWalkable(player) && !current.Boxes.ContainsValue(player), "玩家位置无效");
            Motion = null;
            if (segment.MotionKind == null || segment.MotionKind == "none") return;
            var d = segment.MotionDelta;
            Check(segment.MotionFraction >= 0 && segment.MotionFraction < 1, "存档动画进度无效");
            if (segment.MotionKind == "turn")
            {
                Check(Math.Abs(d.X) == 1, "转向无效");
                Motion = new PuzzleMotion { Kind = "turn", Fraction = (float)segment.MotionFraction, Direction = d.X };
                return;
            }
            var delta = d;
            string pushed = segment.MotionPushed ? current.Boxes.FirstOrDefault(b => b.Value == player + delta).Key : null;
            Check(segment.MotionKind == "move" && Math.Abs(d.X) + Math.Abs(d.Y) == 1
                && current.Validated.Room.TerrainAt(player - delta).CategoryId != TerrainCategories.Void
                && (pushed == null || current.Boxes.TryGetValue(pushed, out var box) && box == player + delta), "存档移动与箱子位置不符");
            Motion = new PuzzleMotion { Kind = "move", Fraction = (float)segment.MotionFraction, Delta = new Vector2Int(d.X, d.Y), Pushed = pushed };
        }
    }
}
