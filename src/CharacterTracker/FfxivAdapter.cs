using Advanced_Combat_Tracker;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace CharacterTracker
{
    internal sealed class FfxivAdapter
    {
        private object repository;
        private MethodInfo getCombatants, getPlayerId, getTerritoryId;
        private string lastError;

        public string LastError { get { return lastError; } }

        public bool Connect()
        {
            try {
                var item = ActGlobals.oFormActMain.ActPlugins.FirstOrDefault(p =>
                    p.pluginFile != null && p.pluginFile.Name.Equals("FFXIV_ACT_Plugin.dll", StringComparison.OrdinalIgnoreCase));
                if (item == null || item.pluginObj == null) { lastError = Localization.T("error.ffxivDisabled"); return false; }
                var prop = item.pluginObj.GetType().GetProperty("DataRepository", BindingFlags.Public | BindingFlags.Instance);
                repository = prop == null ? null : prop.GetValue(item.pluginObj, null);
                if (repository == null) { lastError = Localization.T("error.repository"); return false; }
                var type = repository.GetType();
                getCombatants = type.GetMethod("GetCombatantList", Type.EmptyTypes);
                getPlayerId = type.GetMethod("GetCurrentPlayerID", Type.EmptyTypes);
                getTerritoryId = type.GetMethod("GetCurrentTerritoryID", Type.EmptyTypes);
                if (getCombatants == null || getPlayerId == null || getTerritoryId == null) {
                    lastError = Localization.T("error.api"); return false;
                }
                lastError = null;
                return true;
            } catch (Exception ex) { repository = null; lastError = ex.GetBaseException().Message; return false; }
        }

        public IReadOnlyList<ActorSnapshot> ReadActors(out uint playerId, out uint territoryId)
        {
            playerId = 0; territoryId = 0;
            if (repository == null && !Connect()) return new ActorSnapshot[0];
            try {
                playerId = ToUInt(getPlayerId.Invoke(repository, null));
                territoryId = ToUInt(getTerritoryId.Invoke(repository, null));
                var source = getCombatants.Invoke(repository, null) as IEnumerable;
                var result = new List<ActorSnapshot>();
                if (source != null) foreach (var actor in source) if (actor != null) result.Add(ConvertActor(actor, playerId));
                lastError = null;
                return result;
            } catch (Exception ex) {
                repository = null; lastError = ex.GetBaseException().Message; return new ActorSnapshot[0];
            }
        }

        private static ActorSnapshot ConvertActor(object value, uint playerId)
        {
            int type = ToInt(Get(value, "Type", "ObjectKind"));
            uint id = ToUInt(Get(value, "ID", "Id", "ObjectID", "ObjectId"));
            return new ActorSnapshot {
                Id = id, Name = System.Convert.ToString(Get(value, "Name"), CultureInfo.CurrentCulture) ?? "",
                Type = type, Kind = TrackingMath.KindName(type), Job = ToInt(Get(value, "Job", "ClassJob")),
                X = ToFloat(Get(value, "PosX", "PositionX", "X")),
                Y = ToFloat(Get(value, "PosY", "PositionY", "Y")),
                Z = ToFloat(Get(value, "PosZ", "PositionZ", "Z")),
                Heading = ToFloat(Get(value, "Heading", "Rotation")),
                CurrentHp = ToUInt(Get(value, "CurrentHP", "CurrentHp")), MaxHp = ToUInt(Get(value, "MaxHP", "MaxHp")),
                OwnerId = ToUInt(Get(value, "OwnerID", "OwnerId")), TargetId = ToUInt(Get(value, "TargetID", "TargetId")),
                IsPlayer = id == playerId
            };
        }

        private static object Get(object source, params string[] names)
        {
            var type = source.GetType();
            foreach (string name in names) {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null) try { return prop.GetValue(source, null); } catch { }
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null) try { return field.GetValue(source); } catch { }
            }
            return null;
        }
        private static uint ToUInt(object value) { try { return System.Convert.ToUInt32(value, CultureInfo.InvariantCulture); } catch { return 0; } }
        private static int ToInt(object value) { try { return System.Convert.ToInt32(value, CultureInfo.InvariantCulture); } catch { return 0; } }
        private static float ToFloat(object value) { try { return System.Convert.ToSingle(value, CultureInfo.InvariantCulture); } catch { return 0; } }
    }
}
