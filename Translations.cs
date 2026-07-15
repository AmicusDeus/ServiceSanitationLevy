using System.Collections.Generic;

namespace ServiceSanitationLevy
{
    // English strings for the Options page. Key convention: "<name>.L" = label, "<name>.D" = description.
    // English only at launch; Get() falls back to English (the key's value) for every locale.
    public static class Translations
    {
        public static string Get(string key, string locale) => En.TryGetValue(key, out var v) ? v : key;

        public static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "mod.name", "Service & Sanitation Levy" },
            { "tab.main", "Main" },
            { "group.levy", "Public Service Fee" },
            { "group.general", "General" },

            { "levyEnabled.L", "Enable the Service & Sanitation Levy" },
            { "levyEnabled.D", "A coverage-gated municipal fee: fire, police and disaster bill each occupant a % of its own rent (only where the service reaches the building); garbage bills a % of rent plus a pollution surcharge. Off = vanilla behaviour." },

            { "emergency.L", "Emergency services rate (% of rent)" },
            { "emergency.D", "Each covered emergency service (fire, police, disaster) bills every occupant this percentage of its own monthly rent. A building not covered by a service isn't billed for it." },

            { "garbage.L", "Garbage rate (% of rent)" },
            { "garbage.D", "Garbage bills this percentage of each occupant's rent, plus a pollution surcharge scaled by the same rate so dirty industry pays extra. Gated on the city having at least one garbage facility (recycling centres count). Replaces the native garbage fee." },

            { "achievements.L", "Keep achievements enabled" },
            { "achievements.D", "The game disables achievements while any mod is active; this re-enables them. Safe to leave on even if you run more than one of the split mods." },
        };
    }
}
