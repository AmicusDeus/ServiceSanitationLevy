using Game;
using Game.City;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ServiceSanitationLevy
{
    // Folds the collected levy into the GAME'S OWN treasury math (a real INCOME), instead of poking PlayerMoney out
    // of band (which BudgetApplySystem's continuous recompute swamps). Runs between CityServiceBudgetSystem
    // (recompute) and BudgetApplySystem (apply). Fallback role: writes only the TEMP array (money moves, panel may
    // flicker); when the Harmony display-sync postfix is live it owns BOTH buffers and this system stands down.
    [UpdateAfter(typeof(CityServiceBudgetSystem))]
    [UpdateBefore(typeof(BudgetApplySystem))]
    public partial class BudgetInjectSystem : GameSystemBase
    {
        private CityServiceBudgetSystem m_Budget;
        private FirePoliceLevySystem m_Levy;

        public int InjectedIncome { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Budget = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            m_Levy = World.GetOrCreateSystemManaged<FirePoliceLevySystem>();
        }

        protected override void OnUpdate()
        {
            Setting s = Mod.ActiveSetting;
            if (s == null)
                return;

            // Collected levy -> real INCOME. Folded into TaxResidential (it is a rent-based charge on occupants).
            int feeDaily = s.LevyEnabled ? m_Levy.LevyIncome : 0;
            InjectedIncome = feeDaily;

            if (HarmonyPatcher.BudgetDisplaySyncActive)
                return;
            if (feeDaily == 0)
                return;

            NativeArray<int> income = m_Budget.GetIncomeArray(out JobHandle dInc);
            dInc.Complete();
            income[(int)IncomeSource.TaxResidential] += feeDaily;
        }
    }
}
