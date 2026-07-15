using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace ServiceSanitationLevy
{
    [FileLocation(nameof(ServiceSanitationLevy))]
    public class Setting : ModSetting
    {
        public const string Section = "Main";

        public const string GroupLevy = "Levy";
        public const string GroupGeneral = "General";

        public Setting(IMod mod) : base(mod) { }

        // NOTE: every property carries a C# initializer matching SetDefaults() — the settings-migration failsafe so an
        // older .coc predating a property keeps this value instead of falling back to 0.

        // ---- Public Service Fee (coverage-gated; rent-based, like a property tax) ----
        // Opt-out: ON by default with modest starting rates. Turn OFF for vanilla.
        [SettingsUISection(Section, GroupLevy)]
        public bool LevyEnabled { get; set; } = true;

        // Each covered emergency service (fire/police/disaster) bills every occupant this % of their own monthly rent.
        [SettingsUISlider(min = 0f, max = 25f, step = 1f, unit = "integer")]
        [SettingsUISection(Section, GroupLevy)]
        public float PsfEmergencyPercent { get; set; } = 8f;

        // Garbage bills this % of each occupant's rent, plus a pollution surcharge (scaled by the same rate) so dirty
        // industry pays extra ("polluter pays").
        [SettingsUISlider(min = 0f, max = 25f, step = 1f, unit = "integer")]
        [SettingsUISection(Section, GroupLevy)]
        public float PsfGarbagePercent { get; set; } = 8f;

        // ---- Achievements ----
        [SettingsUISection(Section, GroupGeneral)]
        public bool EnableAchievements { get; set; } = true;

        public override void SetDefaults()
        {
            LevyEnabled = true;
            PsfEmergencyPercent = 8f;
            PsfGarbagePercent = 8f;
            EnableAchievements = true;
        }
    }
}
