using System;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace ServiceSanitationLevy
{
    // Self-test telemetry: once per in-game day (and once right after load), dumps structured [SelfTest] lines with
    // PASS/FAIL assertions to ServiceSanitationLevy.Mod.log so behaviour can be verified by reading the log.
    // Read-only: never mutates game state.
    public partial class DiagnosticsSystem : GameSystemBase
    {
        private SimulationSystem m_Sim;
        private FirePoliceLevySystem m_Levy;
        private EntityQuery m_TimeQuery;
        private EntityQuery m_RenterBuildingQuery;
        private int m_LastDay = int.MinValue;
        private bool m_Primed;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Sim = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_Levy = World.GetOrCreateSystemManaged<FirePoliceLevySystem>();
            m_TimeQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());
            m_RenterBuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<Renter>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
            });
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 256;

        protected override void OnUpdate()
        {
            if (m_TimeQuery.IsEmptyIgnoreFilter)
                return;
            int day = TimeSystem.GetDay(m_Sim.frameIndex, m_TimeQuery.GetSingleton<TimeData>());
            if (!m_Primed)
            {
                m_Primed = true;
                m_LastDay = day - 1;
                return;
            }
            if (day == m_LastDay)
                return;
            m_LastDay = day;
            try { Dump(day); }
            catch (Exception ex) { Mod.log.Warn($"[SelfTest] dump failed: {ex.Message}"); }
        }

        private static string PF(bool ok) => ok ? "PASS" : "FAIL";

        private void Dump(int day)
        {
            Setting s = Mod.ActiveSetting;
            if (s == null)
                return;

            int renterBuildings = m_RenterBuildingQuery.CalculateEntityCount();
            Mod.log.Info($"[SelfTest] day={day} settings: LevyEnabled={s.LevyEnabled} rates={s.PsfEmergencyPercent}%/{s.PsfGarbagePercent}% " +
                         $"achievements={s.EnableAchievements} budgetSync={(HarmonyPatcher.BudgetDisplaySyncActive ? "ACTIVE" : "FALLBACK")}");
            Mod.log.Info($"[SelfTest] PSF: enabled={s.LevyEnabled} monthlyIncome={m_Levy.LevyIncome} renterBuildings={renterBuildings} garbageFacilities={m_Levy.GarbageFacilityCount}");
            if (s.LevyEnabled && renterBuildings > 0)
            {
                NativeArray<Entity> blds = m_RenterBuildingQuery.ToEntityArray(Allocator.Temp);
                int samples = Math.Min(3, blds.Length);
                for (int i = 0; i < samples; i++)
                {
                    var b = m_Levy.ComputeBreakdown(blds[i], s.PsfEmergencyPercent, s.PsfGarbagePercent);
                    int expected = (b.Fire > 0 ? b.Fire : 0) + (b.Police > 0 ? b.Police : 0) + (b.Disaster > 0 ? b.Disaster : 0) + (b.Garbage > 0 ? b.Garbage : 0);
                    Mod.log.Info($"[SelfTest] PSF sample#{i} ent={blds[i].Index}:{blds[i].Version}: rentSum={m_Levy.BuildingRentSum(blds[i])} perService={b.ValueTerm} pollSurcharge={b.PollutionTerm} " +
                                 $"fire={b.Fire} police={b.Police} disaster={b.Disaster} garbage={b.Garbage} total={b.Total} sumCheck:{PF(b.Total == expected)}");
                }
                blds.Dispose();
            }
        }
    }
}
