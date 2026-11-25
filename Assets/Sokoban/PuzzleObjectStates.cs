/**
 * @file PuzzleObjectStates.cs
 * @brief Synchronizes visual parts and passable remnants from the current room's saved state records. Samples configured recovery edges using the same clock as the gameplay recovery.
 */
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public sealed partial class PuzzleHost
    {
        readonly Dictionary<(string, string), GameObject> remnants = new Dictionary<(string, string), GameObject>();
        readonly Dictionary<string, ForestRecovery> stateEffects = new Dictionary<string, ForestRecovery>();

        void SyncObjectStates(string roomId, Transform group)
        {
            var records = Run.ObjectStateRecords(roomId);
            foreach (Transform part in group)
            {
                if (!part.TryGetComponent<ObjectStateView>(out var visual)) continue;
                visual.Show(Run.ObjectState(roomId, part.name));
                var key = (roomId, part.name);
                var state = records[part.name];
                if ((string)state?["remnant"] is string model)
                {
                    if (!remnants.TryGetValue(key, out var remnant))
                    {
                        var prefab = ObjectStateProfile.Prefab(model);
                        remnant = ObjectStateView.Create((JObject)Run.Rooms[roomId], group, prefab, prefab == null ? new JObject { ["model"] = model } : null);
                        remnant.name = "遗留物 · " + part.name;
                        remnants[key] = remnant;
                    }
                    remnant.transform.position = Position(RoomFile.Cell(state["remnantCell"]));
                }
                else if (remnants.TryGetValue(key, out var remnant))
                {
                    remnant.SetActive(false);
                    Destroy(remnant);
                    remnants.Remove(key);
                }
            }
        }

        void SampleObjectStateRecovery(float fraction)
        {
            foreach (var item in RoomFile.Objects(Run.Room))
            {
                string id = (string)item["id"];
                var edge = Run.StateRestoreEdge(id);
                if (edge == null) continue;
                string arrive = (string)edge["arriveAnimation"], depart = (string)edge["departAnimation"];
                if (edge["arrive"] == null && edge["depart"] == null && arrive == null && depart == null) continue;
                var owner = objects[id];
                Vector3 home = Run.Boxes.ContainsKey(id) ? Position(Run.RecoveryCell(id)) : owner.position;
                if (!stateEffects.TryGetValue(id, out var effect))
                {
                    var group = groups[Run.ActiveId];
                    var visual = owner.GetComponent<ObjectStateView>();
                    var grown = edge["arrive"] != null || arrive != null ? visual.CreateVariant((string)edge["to"], group, arrive) : null;
                    var outgoing = depart != null ? visual.CreateVariant((string)edge["from"], group, depart) : null;
                    effect = new ForestRecovery(group, owner, home, grown, (string)edge["depart"] == "forestDecay", outgoing, arrive == null);
                    stateEffects.Add(id, effect);
                }
                effect.Sample(fraction);
                if (remnants.TryGetValue((Run.ActiveId, id), out var remnant)) remnant.SetActive(fraction < .605f);
            }
        }
    }
}
