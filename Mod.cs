using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace ServiceSanitationLevy
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(ServiceSanitationLevy)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public static Setting ActiveSetting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            // Harmony: budget display-sync (Economy>Budget flicker fix). Falls back safely to TEMP-only injection.
            HarmonyPatcher.Apply();

            ActiveSetting = new Setting(this);
            ActiveSetting.RegisterInOptionsUI();

            var lm = GameManager.instance.localizationManager;
            foreach (var locale in lm.GetSupportedLocales())
                lm.AddSource(locale, new LocaleSource(ActiveSetting, locale));

            AssetDatabase.global.LoadSettings(nameof(ServiceSanitationLevy), ActiveSetting, new Setting(this));
            // Persist every settings change to disk the moment it is applied (survives a crash / non-clean exit).
            ActiveSetting.onSettingsApplied += OnSettingsApplied;

            updateSystem.UpdateAt<GarbageFeeOffSystem>(SystemUpdatePhase.GameSimulation);  // fold native garbage fee off
            updateSystem.UpdateAt<FirePoliceLevySystem>(SystemUpdatePhase.GameSimulation); // charge the levy
            updateSystem.UpdateAt<AchievementEnablerSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<BudgetInjectSystem>(SystemUpdatePhase.GameSimulation);   // credit the treasury
            updateSystem.UpdateAt<DiagnosticsSystem>(SystemUpdatePhase.GameSimulation);    // [SelfTest] log lines

            // UI bridge — the levy panel + per-building PSF breakdown + budget breakdown.
            updateSystem.UpdateAt<LevyUISystem>(SystemUpdatePhase.UIUpdate);

            log.Info("Service & Sanitation Levy loaded.");
        }

        // Persist a settings change to disk as soon as it is applied (guard: ApplyAndSave re-raises onSettingsApplied).
        private static bool s_savingReentrant;
        private static void OnSettingsApplied(Game.Settings.Setting setting)
        {
            if (s_savingReentrant)
                return;
            s_savingReentrant = true;
            try { ActiveSetting?.ApplyAndSave(); }
            finally { s_savingReentrant = false; }
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));

            HarmonyPatcher.Remove();

            if (ActiveSetting != null)
            {
                ActiveSetting.onSettingsApplied -= OnSettingsApplied;
                ActiveSetting.UnregisterInOptionsUI();
                ActiveSetting = null;
            }
        }
    }
}
