/**
 * @file PuzzleObjectStates.cs
 * @brief 按当前房间的对象状态同步外观与残留物，状态经 D 组提供的 PuzzleRun.ObjectStatesIn(roomId) 与 SetObjectState(roomId, objectId, state) 读写；残留物是纯表现，不参与规则。
 * SyncObjectStates(roomId, group)：逐个对象显示当前状态外观，并按状态表决定残留物的出现与回收。
 * ApplyObjectState(roomId, objectId, state, group)：经 SetObjectState 写入后立即重新同步该房间的外观。
 * SampleObjectStateRecovery(fraction)：恢复演出期间按同一时钟控制残留物的可见窗口，状态回到初始后由同步回收。
 * RemnantOf(table, state)：状态不是初始状态时，返回从初始状态到该状态且带残留物的转换，没有则为空。
 */
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban
{
    public sealed partial class PuzzleHost
    {
        readonly Dictionary<(string, string), GameObject> remnants = new Dictionary<(string, string), GameObject>();

        static StateTransition RemnantOf(ObjectStateTable table, string state) =>
            table == null || state == null || state == table.Initial ? null
                : table.Transitions.FirstOrDefault(t => t.From == table.Initial && t.To == state && t.Remnant != null);

        void SyncObjectStates(string roomId, Transform group)
        {
            ApplyBehavior();
            var states = Run.ObjectStatesIn(roomId);
            foreach (Transform part in group)
            {
                if (!part.TryGetComponent<ObjectStateView>(out var visual)) continue;
                string state = states != null && states.TryGetValue(part.name, out var value) ? value : null;
                visual.Show(state);
                var key = (roomId, part.name);
                var transition = RemnantOf(visual.Table, state);
                if (transition != null)
                {
                    if (!remnants.TryGetValue(key, out var remnant) || !remnant)
                    {
                        remnant = visual.CreateRemnant(transition.Remnant, group);
                        remnant.name = "残留物 · " + part.name;
                        remnants[key] = remnant;
                    }
                    remnant.transform.position = visual.Home;
                    remnant.SetActive(true);
                }
                else if (remnants.TryGetValue(key, out var stale))
                {
                    if (stale) { stale.SetActive(false); Destroy(stale); }
                    remnants.Remove(key);
                }
            }
        }

        void ApplyObjectState(string roomId, string objectId, string state, Transform group)
        {
            Run.SetObjectState(roomId, objectId, state);
            SyncObjectStates(roomId, group);
        }

        void SampleObjectStateRecovery(float fraction)
        {
            foreach (var pair in remnants)
                if (pair.Key.Item1 == Run.ActiveId && pair.Value) pair.Value.SetActive(fraction < .605f);
        }
    }
}
