using System;
using Colossal.UI.Binding;
using Game.Buildings;
using Game.Prefabs;
using Game.Tools;
using Game.UI;
using Unity.Entities;

namespace ServiceSanitationLevy
{
    // Backs the Levy panel (enable + two rate steppers + live income) and the per-building PSF breakdown shown in the
    // selected building's Employees/Residents info sections. Bindings (group "LevyParams") consumed by
    // UI/src/mods/levy.tsx.
    public partial class LevyUISystem : UISystemBase
    {
        private const string Group = "LevyParams";

        private FirePoliceLevySystem m_LevySystem;
        private ToolSystem m_ToolSystem;
        private EntityQuery m_TimeSettingsQuery;

        private GetterValueBinding<bool> m_LevyEnabled;
        private GetterValueBinding<float> m_LevyBase;
        private GetterValueBinding<float> m_LevyPollution;
        private GetterValueBinding<int> m_LevyIncome;
        private GetterValueBinding<int> m_HoursPerMonth;
        private int m_LevyIncomeValue;

        private GetterValueBinding<int> m_SelLevy;
        private GetterValueBinding<int> m_SelPsfValue;
        private GetterValueBinding<int> m_SelPsfPoll;
        private GetterValueBinding<int> m_SelPsfFire;
        private GetterValueBinding<int> m_SelPsfPolice;
        private GetterValueBinding<int> m_SelPsfDisaster;
        private GetterValueBinding<int> m_SelPsfGarbage;
        private FirePoliceLevySystem.PsfBreakdown m_SelBreakdown;
        private bool m_SelBilled;
        private int m_Tick;

        private static Setting S => Mod.ActiveSetting;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_LevySystem = World.GetOrCreateSystemManaged<FirePoliceLevySystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_TimeSettingsQuery = GetEntityQuery(ComponentType.ReadOnly<TimeSettingsData>());

            m_LevyEnabled = new GetterValueBinding<bool>(Group, "levyEnabled", () => S != null && S.LevyEnabled);
            m_LevyBase = new GetterValueBinding<float>(Group, "levyBase", () => S != null ? S.PsfEmergencyPercent : 0f);
            m_LevyPollution = new GetterValueBinding<float>(Group, "levyPollution", () => S != null ? S.PsfGarbagePercent : 0f);
            m_LevyIncome = new GetterValueBinding<int>(Group, "levyIncome", () => m_LevyIncomeValue);
            m_HoursPerMonth = new GetterValueBinding<int>(Group, "hoursPerMonth", HoursPerMonth);
            AddBinding(m_LevyEnabled);
            AddBinding(m_LevyBase);
            AddBinding(m_LevyPollution);
            AddBinding(m_LevyIncome);
            AddBinding(m_HoursPerMonth);
            AddBinding(new TriggerBinding<bool>(Group, "setLevyEnabled", v => { if (S != null && S.LevyEnabled != v) { S.LevyEnabled = v; S.ApplyAndSave(); } }));
            AddBinding(new TriggerBinding<float>(Group, "setLevyBase", v => { v = Math.Max(0f, Math.Min(25f, v)); if (S != null && S.PsfEmergencyPercent != v) { S.PsfEmergencyPercent = v; S.ApplyAndSave(); } }));
            AddBinding(new TriggerBinding<float>(Group, "setLevyPollution", v => { v = Math.Max(0f, Math.Min(25f, v)); if (S != null && S.PsfGarbagePercent != v) { S.PsfGarbagePercent = v; S.ApplyAndSave(); } }));

            m_SelLevy = new GetterValueBinding<int>(Group, "selLevy", () => m_SelBilled ? m_SelBreakdown.Total : -1);
            m_SelPsfValue = new GetterValueBinding<int>(Group, "selPsfValue", () => m_SelBreakdown.ValueTerm);
            m_SelPsfPoll = new GetterValueBinding<int>(Group, "selPsfPoll", () => m_SelBreakdown.PollutionTerm);
            m_SelPsfFire = new GetterValueBinding<int>(Group, "selPsfFire", () => m_SelBreakdown.Fire);
            m_SelPsfPolice = new GetterValueBinding<int>(Group, "selPsfPolice", () => m_SelBreakdown.Police);
            m_SelPsfDisaster = new GetterValueBinding<int>(Group, "selPsfDisaster", () => m_SelBreakdown.Disaster);
            m_SelPsfGarbage = new GetterValueBinding<int>(Group, "selPsfGarbage", () => m_SelBreakdown.Garbage);
            AddBinding(m_SelLevy);
            AddBinding(m_SelPsfValue);
            AddBinding(m_SelPsfPoll);
            AddBinding(m_SelPsfFire);
            AddBinding(m_SelPsfPolice);
            AddBinding(m_SelPsfDisaster);
            AddBinding(m_SelPsfGarbage);

            if (S != null)
                S.onSettingsApplied += OnSettingsApplied;
        }

        private int HoursPerMonth()
        {
            if (m_TimeSettingsQuery.IsEmptyIgnoreFilter)
                return 24;
            int dpm = m_TimeSettingsQuery.GetSingleton<TimeSettingsData>().m_DaysPerYear / 12;
            if (dpm < 1) dpm = 1;
            return dpm * 24;
        }

        // Recompute the selected building's PSF breakdown. Only occupied (renter) buildings are billed.
        private void RefreshSelectedFee()
        {
            m_SelBilled = false;
            m_SelBreakdown = default;
            Setting s = S;
            if (s != null && s.LevyEnabled && m_LevySystem != null && m_ToolSystem != null)
            {
                Entity e = m_ToolSystem.selected;
                if (e != Entity.Null && EntityManager.Exists(e)
                    && EntityManager.HasComponent<Building>(e) && EntityManager.HasComponent<PrefabRef>(e)
                    && EntityManager.HasBuffer<Renter>(e) && EntityManager.GetBuffer<Renter>(e, isReadOnly: true).Length > 0)
                {
                    m_SelBreakdown = m_LevySystem.ComputeBreakdown(e, s.PsfEmergencyPercent, s.PsfGarbagePercent);
                    m_SelBilled = true;
                }
            }
            m_SelLevy.Update();
            m_SelPsfValue.Update();
            m_SelPsfPoll.Update();
            m_SelPsfFire.Update();
            m_SelPsfPolice.Update();
            m_SelPsfDisaster.Update();
            m_SelPsfGarbage.Update();
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            m_Tick++;
            if (m_Tick % 16 == 0)
                RefreshSelectedFee();
            if (m_Tick % 32 == 0)
                Refresh();
        }

        private void Refresh()
        {
            if (m_LevySystem != null)
            {
                m_LevyIncomeValue = m_LevySystem.LevyIncome;
                m_LevyIncome.Update();
            }
            m_HoursPerMonth.Update();
        }

        private void OnSettingsApplied(Game.Settings.Setting setting)
        {
            m_LevyEnabled.Update();
            m_LevyBase.Update();
            m_LevyPollution.Update();
            Refresh();
        }

        protected override void OnDestroy()
        {
            if (S != null)
                S.onSettingsApplied -= OnSettingsApplied;
            base.OnDestroy();
        }
    }
}
