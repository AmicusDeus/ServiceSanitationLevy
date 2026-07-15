import { useLocalization } from "cs2/l10n";

// Panel localization: strings go through t(key, fallback, vars). Ids are "ServiceSanitationLevy.ui.<key>".
export function useT() {
    const loc = useLocalization();
    return (key: string, fallback: string, vars?: Record<string, string | number>) => {
        let s = fallback;
        try {
            const r = loc && loc.translate("ServiceSanitationLevy.ui." + key, fallback);
            if (r) s = r;
        } catch { /* fall back to English */ }
        if (vars) {
            for (const k in vars) s = s.split("{" + k + "}").join(String(vars[k]));
        }
        return s;
    };
}
