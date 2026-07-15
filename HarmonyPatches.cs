using System;
using System.Reflection;
using Game.City;
using Game.Simulation;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ServiceSanitationLevy
{
    // One Harmony patch: BUDGET DISPLAY SYNC (fix the Economy>Budget panel flicker). CityServiceBudgetSystem is
    // DOUBLE-BUFFERED: a per-tick recompute job fills the TEMP arrays (BudgetApplySystem reads these to move the
    // treasury), and OnUpdate copies TEMP -> COMMITTED (every UI getter reads these). The mod's levy income only
    // ever reached TEMP, so the UI — reading COMMITTED — flickered. This postfix, after OnUpdate, adds the delta to
    // TEMP then re-copies TEMP -> COMMITTED so both buffers agree.
    //
    // Failsafe: wrapped; a failure keeps BudgetDisplaySyncActive false — the mod then runs BudgetInjectSystem's
    // TEMP-only fallback (money still moves, panel flickers). Never worse than unpatched.
    public static class HarmonyPatcher
    {
        public const string Id = "ServiceSanitationLevy";

        public static bool BudgetDisplaySyncActive { get; private set; }

        private static Harmony s_Harmony;
        private static FieldInfo s_fIncome, s_fExpenses, s_fIncomeTemp, s_fExpensesTemp, s_fTempDeps;
        private static bool s_budgetSyncThrewLogged;

        public static void Apply()
        {
            try { s_Harmony = new Harmony(Id); }
            catch (Exception ex)
            {
                Mod.log.Warn($"[Harmony] init failed — display-sync skipped (money still moves via TEMP): {ex.Message}");
                return;
            }
            ApplyBudgetDisplaySync();
        }

        public static void Remove()
        {
            try { s_Harmony?.UnpatchAll(Id); } catch { /* best effort */ }
            BudgetDisplaySyncActive = false;
        }

        private static void ApplyBudgetDisplaySync()
        {
            try
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
                s_fIncome       = typeof(CityServiceBudgetSystem).GetField("m_Income", F);
                s_fExpenses     = typeof(CityServiceBudgetSystem).GetField("m_Expenses", F);
                s_fIncomeTemp   = typeof(CityServiceBudgetSystem).GetField("m_IncomeTemp", F);
                s_fExpensesTemp = typeof(CityServiceBudgetSystem).GetField("m_ExpensesTemp", F);
                s_fTempDeps     = typeof(CityServiceBudgetSystem).GetField("m_TempArrayDeps", F);
                if (s_fIncome == null || s_fExpenses == null || s_fIncomeTemp == null || s_fExpensesTemp == null || s_fTempDeps == null)
                    throw new MissingFieldException("CityServiceBudgetSystem budget-array field(s) not found");

                MethodInfo target = AccessTools.Method(typeof(CityServiceBudgetSystem), "OnUpdate");
                MethodInfo postfix = AccessTools.Method(typeof(HarmonyPatcher), nameof(BudgetOnUpdatePostfix));
                if (target == null || postfix == null)
                    throw new MissingMethodException("CityServiceBudgetSystem.OnUpdate not found");
                s_Harmony.Patch(target, postfix: new HarmonyMethod(postfix));

                BudgetDisplaySyncActive = true;
                Mod.log.Info("[Harmony] CityServiceBudgetSystem.OnUpdate postfix installed — budget display sync ACTIVE (flicker fix)");
            }
            catch (Exception ex)
            {
                BudgetDisplaySyncActive = false;
                Mod.log.Warn($"[Harmony] budget display-sync patch FAILED — TEMP-only injection (money moves, panel may flicker): {ex.Message}");
            }
        }

        // Runs after every CityServiceBudgetSystem.OnUpdate. Adds the mod's levy (income) delta into BOTH the TEMP
        // array (apply-side) and the COMMITTED array (display-side), so exactly one copy lives in both.
        private static void BudgetOnUpdatePostfix(CityServiceBudgetSystem __instance)
        {
            if (!BudgetDisplaySyncActive || __instance == null)
                return;
            try
            {
                World world = __instance.World;
                Setting s = Mod.ActiveSetting;
                if (world == null || s == null)
                    return;

                FirePoliceLevySystem levy = world.GetExistingSystemManaged<FirePoliceLevySystem>();
                int feeDaily = (s.LevyEnabled && levy != null) ? levy.LevyIncome : 0;
                if (feeDaily == 0)
                    return;

                NativeArray<int> expensesTemp = (NativeArray<int>)s_fExpensesTemp.GetValue(__instance);
                NativeArray<int> incomeTemp   = (NativeArray<int>)s_fIncomeTemp.GetValue(__instance);
                NativeArray<int> expenses     = (NativeArray<int>)s_fExpenses.GetValue(__instance);
                NativeArray<int> income       = (NativeArray<int>)s_fIncome.GetValue(__instance);
                JobHandle deps = (JobHandle)s_fTempDeps.GetValue(__instance);
                deps.Complete();

                incomeTemp[(int)IncomeSource.TaxResidential] += feeDaily;

                // Re-sync COMMITTED to the now-injected TEMP: exactly one copy of the delta in both, same tick.
                expensesTemp.CopyTo(expenses);
                incomeTemp.CopyTo(income);
            }
            catch (Exception ex)
            {
                BudgetDisplaySyncActive = false;
                if (!s_budgetSyncThrewLogged)
                {
                    s_budgetSyncThrewLogged = true;
                    Mod.log.Warn($"[Harmony] budget display-sync postfix threw — reverting to TEMP-only injection: {ex}");
                }
            }
        }
    }
}
