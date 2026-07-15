import { useState, useEffect } from "react";
import * as ReactDOM from "react-dom";
import { bindValue, useValue, trigger } from "cs2/api";
import { getModule } from "cs2/modding";
import { FloatingButton } from "cs2/ui";
import { useT } from "mods/i18n";
import { FONT, TEXT, panelTitle, sectionTitle } from "mods/ui-tokens";
import ICON from "../ssl-icon.svg";

// Service & Sanitation Levy UI — the levy panel (rates + income), the per-building PSF breakdown row (Employees /
// Residents sections), the toolbar button, and the Budget "Safety & Sanitation Levy" detail sub-item + hover box.
const G = "LevyParams";

const levyEnabled$ = bindValue<boolean>(G, "levyEnabled", false);
const levyBase$ = bindValue<number>(G, "levyBase", 0);
const levyPollution$ = bindValue<number>(G, "levyPollution", 0);
const levyIncome$ = bindValue<number>(G, "levyIncome", 0);
const hoursPerMonth$ = bindValue<number>(G, "hoursPerMonth", 24);
// Per-building breakdown for the selected-building info panel. selLevy -1 = hide; per-service -1 = not served.
const selLevy$ = bindValue<number>(G, "selLevy", -1);
const selPsfValue$ = bindValue<number>(G, "selPsfValue", 0);
const selPsfPoll$ = bindValue<number>(G, "selPsfPoll", 0);
const selPsfFire$ = bindValue<number>(G, "selPsfFire", -1);
const selPsfPolice$ = bindValue<number>(G, "selPsfPolice", -1);
const selPsfDisaster$ = bindValue<number>(G, "selPsfDisaster", -1);
const selPsfGarbage$ = bindValue<number>(G, "selPsfGarbage", -1);

const LEVY_BASE_MAX = 25, LEVY_BASE_STEP = 1, LEVY_POLL_MAX = 25, LEVY_POLL_STEP = 1;

const fmt = (n: number) => Math.round(Math.abs(n)).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");

// ---- toolbar button + panel plumbing --------------------------------------------------------------------------
const PANEL_ID = "levy";
let _open = false;
const _subs = new Set<() => void>();
function _notify() { _subs.forEach((f) => f()); }
// Registered in a shared page-window registry so opening THIS panel closes the OTHER mods' floating panels
// (native CS2 = one toolbar panel at a time). _forceClose only touches this panel (no re-broadcast).
function _forceClose() { if (_open) { _open = false; _notify(); } }
(function () { const w = window as any; if (!w.__csFloatingPanels) w.__csFloatingPanels = {}; w.__csFloatingPanels[PANEL_ID] = _forceClose; })();
function setOpen(v: boolean) {
    if (v) { const reg = (window as any).__csFloatingPanels; if (reg) { for (const id in reg) { if (id !== PANEL_ID) { try { reg[id](); } catch { } } } } }
    if (_open !== v) { _open = v; _notify(); }
}
function useOpen() {
    const [, force] = useState(0);
    useEffect(() => { const f = () => force((x) => x + 1); _subs.add(f); return () => { _subs.delete(f); }; }, []);
    return _open;
}
const CloseGlyph = ({ onClick }: { onClick: () => void }) => (
    <button onClick={onClick} style={{ cursor: "pointer", width: "24rem", height: "24rem", border: "none", background: "transparent", padding: 0, pointerEvents: "auto" } as any}>
        <div style={{
            width: "24rem", height: "24rem", margin: "auto", backgroundColor: "var(--textColor)",
            maskImage: "url(Media/Glyphs/Close.svg)", WebkitMaskImage: "url(Media/Glyphs/Close.svg)",
            maskSize: "contain", WebkitMaskSize: "contain", maskRepeat: "no-repeat", WebkitMaskRepeat: "no-repeat",
            maskPosition: "center", WebkitMaskPosition: "center",
        } as any} />
    </button>
);

const NumField = ({ value, min, max, onCommit, width }: { value: number; min: number; max: number; onCommit: (n: number) => void; width?: number }) => {
    const [text, setText] = useState<string | null>(null);
    const commit = () => {
        if (text === null) return;
        const n = parseInt(text.replace(/[^0-9]/g, ""), 10);
        if (!isNaN(n)) onCommit(Math.max(min, Math.min(max, n)));
        setText(null);
    };
    return (
        <input
            type="text"
            value={text !== null ? text : String(Math.round(value))}
            onChange={(e: any) => setText(String(e.target.value))}
            onBlur={commit}
            onKeyDown={(e: any) => { if (e.key === "Enter") commit(); }}
            style={{ width: (width || 48) + "rem", textAlign: "center", fontSize: "14rem", color: "white", background: "rgba(255,255,255,0.08)", borderRadius: "4rem", padding: "3rem 4rem", border: "1rem solid rgba(255,255,255,0.15)" }}
        />
    );
};

const LevyRateRow = ({ label, value, max, step, triggerKey, hint }: { label: string; value: number; max: number; step: number; triggerKey: string; hint: string }) => {
    const set = (v: number) => { const snapped = Math.max(0, Math.min(max, Math.round(v / step) * step)); if (snapped !== value) trigger(G, triggerKey, snapped); };
    const btn = { cursor: "pointer", width: "26rem", height: "24rem", fontSize: "15rem", color: "white", background: "rgba(255,255,255,0.12)", borderRadius: "4rem" } as const;
    return (
        <div style={{ display: "flex", alignItems: "center", padding: "5rem 14rem" }}>
            <div style={{ width: "150rem" }}>
                <div style={{ fontSize: "14rem" }}>{label}</div>
                <div style={{ fontSize: "11rem", opacity: 0.5 }}>{hint}</div>
            </div>
            <div style={{ flex: 1 }} />
            <button style={btn} onClick={() => set(value - step)}>−</button>
            <NumField value={value} min={0} max={max} width={48} onCommit={(n) => trigger(G, triggerKey, n)} />
            <button style={btn} onClick={() => set(value + step)}>+</button>
        </div>
    );
};

const LevySection = () => {
    const enabled = useValue(levyEnabled$);
    const base = useValue(levyBase$) as number;
    const poll = useValue(levyPollution$) as number;
    const income = useValue(levyIncome$) as number;
    const hpm = useValue(hoursPerMonth$) as number;
    const show = enabled && income > 0;
    const t = useT();
    return (
        <div>
            <div style={{ display: "flex", alignItems: "center", padding: "4rem 14rem 8rem" }}>
                <div style={{ flex: 1, ...sectionTitle, opacity: 0.9 }}>{t("psfHeader", "SAFETY & SANITATION LEVY")}</div>
                <div style={{ fontSize: "13rem", color: show ? "rgb(120, 210, 130)" : "rgba(255,255,255,0.6)" }}>
                    {show ? "+₡" + fmt(income / (hpm || 24)) + " /h" : t("noIncome", "no income")}
                </div>
            </div>
            <div style={{ display: "flex", alignItems: "center", padding: "0rem 14rem 8rem" }}>
                <button
                    onClick={() => trigger(G, "setLevyEnabled", !enabled)}
                    style={{ cursor: "pointer", padding: "5rem 12rem", borderRadius: "4rem", fontSize: "13rem", color: "white", background: enabled ? "rgba(60, 160, 90, 0.9)" : "rgba(120, 120, 120, 0.6)" }}
                >
                    {enabled ? t("feeOn", "Fee: ON") : t("feeOff", "Fee: OFF")}
                </button>
            </div>
            {enabled && (
                <>
                    <LevyRateRow label={t("emergencyRate", "Emergency rate")} hint={t("emergencyRateHint", "% of rent, each covered service")} value={base} max={LEVY_BASE_MAX} step={LEVY_BASE_STEP} triggerKey="setLevyBase" />
                    <LevyRateRow label={t("garbageRate", "Garbage rate")} hint={t("garbageRateHint", "% of rent + pollution surcharge")} value={poll} max={LEVY_POLL_MAX} step={LEVY_POLL_STEP} triggerKey="setLevyPollution" />
                    <div style={{ padding: "2rem 14rem 2rem", fontSize: "11rem", opacity: 0.7 }}>
                        {t("ratesLine", "Each emergency service (fire/police/disaster) bills {a}% of every occupant's monthly rent · Garbage: {b}% of rent, plus a pollution surcharge for dirty buildings.", { a: fmt(base), b: fmt(poll) })}
                    </div>
                    <div style={{ padding: "0rem 14rem 8rem", fontSize: "11rem", opacity: 0.55 }}>
                        {t("psfExplainer", "Fees scale with each occupant's own rent, like a property tax. A service bills only where it actually operates (no station/facility = that share is free). Select a building to see its breakdown.")}
                    </div>
                </>
            )}
        </div>
    );
};

// Per-building Public Service Fee breakdown, injected into the Employees/Residents info sections.
export const LevyInfoRow = () => {
    const levy = useValue(selLevy$) as number;
    const val = useValue(selPsfValue$) as number;
    const poll = useValue(selPsfPoll$) as number;
    const ePct = useValue(levyBase$) as number;
    const gPct = useValue(levyPollution$) as number;
    const fire = useValue(selPsfFire$) as number;
    const police = useValue(selPsfPolice$) as number;
    const disaster = useValue(selPsfDisaster$) as number;
    const garbage = useValue(selPsfGarbage$) as number;
    const shown = levy >= 0;
    const t = useT();
    if (!shown) return null;

    const perMonth = t("perMonth", " /mo");
    const ServiceLine = ({ name, amt }: { name: string; amt: number }) => (
        <div style={{ display: "flex", alignItems: "center", padding: "1rem 0" }}>
            <div style={{ flex: 1, opacity: 0.75 }}>{name}</div>
            <div style={{ color: amt >= 0 ? "rgb(232,150,90)" : "rgba(255,255,255,0.4)" }}>
                {amt >= 0 ? "₡" + fmt(amt) + perMonth : t("notServed", "not served — free")}
            </div>
        </div>
    );
    return (
        <div style={{ padding: "8rem 16rem", fontSize: "13rem" }}>
            <div style={{ display: "flex", alignItems: "center", fontSize: "14rem" }}>
                <div style={{ flex: 1 }}>{t("psfRowTitle", "Safety & Sanitation Levy")}</div>
                <div style={{ color: levy > 0 ? "rgb(232,150,90)" : "rgba(255,255,255,0.5)" }}>{"₡" + fmt(levy) + perMonth}</div>
            </div>
            <div style={{ fontSize: "11rem", opacity: 0.5, padding: "2rem 0 4rem" }}>
                {t("psfBase", "emergency services: ₡{a} each ({e}% of occupants' rent) · garbage: {g}% of rent + ₡{b} pollution surcharge", { a: fmt(val), b: fmt(poll), e: fmt(ePct), g: fmt(gPct) })}
            </div>
            <ServiceLine name={t("fireProtection", "Fire protection")} amt={fire} />
            <ServiceLine name={t("police", "Police")} amt={police} />
            <ServiceLine name={t("disasterResponse", "Disaster response")} amt={disaster} />
            <ServiceLine name={t("garbageCollection", "Garbage collection")} amt={garbage} />
        </div>
    );
};

export const LevyButton = () => {
    const t = useT();
    return <FloatingButton src={ICON} tooltipLabel={t("buttonTooltip", "Service & Sanitation Levy")} onSelect={() => setOpen(!_open)} />;
};

export const LevyPanelHost = () => {
    const open = useOpen();
    const t = useT();
    if (!open) return null;
    return (
        <div style={{
            position: "fixed", top: "90rem", right: "56rem", width: "480rem", zIndex: 99999, pointerEvents: "auto",
            background: "rgba(13, 21, 33, 0.97)", borderRadius: "6rem", display: "flex", flexDirection: "column",
            color: TEXT, boxShadow: "0 4rem 24rem rgba(0,0,0,0.5)",
        }}>
            <div style={{ display: "flex", alignItems: "center", padding: "10rem 14rem", borderBottom: "1rem solid rgba(255,255,255,0.12)" }}>
                <div style={{ flex: 1, ...panelTitle }}>{t("panelTitle", "SERVICE & SANITATION LEVY")}</div>
                <CloseGlyph onClick={() => setOpen(false)} />
            </div>
            <div style={{ padding: "10rem 0 10rem", maxHeight: "860rem", overflowY: "auto" }}>
                <LevySection />
            </div>
        </div>
    );
};

// ---- budget detail sub-item (Taxes -> Safety & Sanitation Levy) ------------------------------------------------
export const BudgetDetailInject = ({ Original, detailProps }: { Original: any; detailProps: any }) => {
    const enabled = useValue(levyEnabled$);
    const income = useValue(levyIncome$) as number; // monthly, positive

    const item = detailProps && detailProps.item;
    const id = item && item.id;
    if (!item || id !== "Taxes" || !Array.isArray(item.sources) || !Array.isArray(detailProps.values))
        return <Original {...detailProps} />;

    try {
        const values = detailProps.values.slice();
        const sources = item.sources.slice();
        let residual = 0;
        if (enabled && income > 0) {
            const idx = values.length;
            values.push(income);
            sources.push({ __Type: "Game.UI.InGame.BudgetSource", id: "SSLSafetyLevy", index: idx });
            residual += income;
        }
        if (residual !== 0 && values.length > 0) values[0] = (values[0] || 0) - residual;
        return <Original {...detailProps} item={{ ...item, sources }} values={values} />;
    } catch {
        return <Original {...detailProps} />;
    }
};

// ---- hover box for the injected "Safety & Sanitation Levy" budget row ------------------------------------------
export const BudgetRowHoverLayer = () => {
    const t = useT();
    const [box, setBox] = useState<{ row: any; left: number; top: number; width: number } | null>(null);
    useEffect(() => {
        const rows = [
            { label: t("psfRowTitle", "Safety & Sanitation Levy"), icon: "Media/Game/Icons/ServiceFees.svg", title: t("psfHeader", "SAFETY & SANITATION LEVY"), text: t("hoverPsf", "A monthly municipal bill charged like a property tax: every household and company pays a percentage of its own rent for each service that actually operates at its building — fire, police and disaster response each bill the emergency rate; garbage bills the garbage rate plus a pollution surcharge for dirty buildings. No coverage or no facility means that share is free. Collected from occupant balances and credited to your treasury as this revenue.") },
        ];
        const findRow = (el: any) => {
            let c = el, best: any = null;
            for (let i = 0; i < 5 && c; i++) {
                const tx = ((c.textContent || "") as string).trim();
                for (const r of rows) {
                    if (r.label && tx.indexOf(r.label) === 0 && tx.length < r.label.length + 26) {
                        if (!best || tx.length > best.len) best = { row: r, el: c, len: tx.length };
                    }
                }
                c = c.parentElement;
            }
            return best;
        };
        let infoSel: string | null = null;
        const infoColumn = () => {
            if (infoSel === null) {
                try {
                    const cls: any = getModule("game-ui/game/components/economy-panel/budget-page/budget-page.module.scss", "classes");
                    infoSel = cls && cls.infoColumn ? "." + String(cls.infoColumn).split(" ")[0] : "";
                } catch { infoSel = ""; }
            }
            return infoSel;
        };
        const onOver = (e: any) => {
            try {
                const hit = findRow(e.target);
                if (!hit) { setBox((b) => (b ? null : b)); return; }
                let vh = 1080, vw = 1920;
                try { vh = (window as any).innerHeight || 1080; vw = (window as any).innerWidth || 1920; } catch { }
                let left = Math.round(vw * 0.655), top = Math.round(vh * 0.27), width = 360;
                try {
                    const sel = infoColumn();
                    const col = sel ? document.querySelector(sel) : null;
                    if (col) { const cr = (col as any).getBoundingClientRect(); left = Math.round(cr.left + 10); top = Math.round(cr.top + 8); width = Math.max(240, Math.round(cr.width - 20)); }
                } catch { }
                setBox({ row: hit.row, left, top, width });
            } catch { }
        };
        document.addEventListener("mouseover", onOver, true);
        return () => document.removeEventListener("mouseover", onOver, true);
    }, []);

    if (!box) return null;
    const row = box.row;
    const el = (
        <div style={{
            position: "fixed", left: box.left + "px", top: box.top + "px", width: box.width + "rem",
            boxSizing: "border-box", zIndex: 99999, pointerEvents: "none",
            display: "flex", flexDirection: "column", alignItems: "stretch",
        } as any}>
            <div style={{ width: "96rem", height: "96rem", padding: "4rem", boxSizing: "border-box", backgroundColor: "var(--panelColorDark)", borderRadius: "8rem", marginBottom: "6rem" } as any}>
                <img src={row.icon} style={{ width: "100%", height: "100%" }} />
            </div>
            <div style={{ fontSize: "var(--fontSizeXL)", fontWeight: "bold", textTransform: "uppercase", color: "var(--accentColorLight)", lineHeight: "1.2", margin: "12rem 0", wordWrap: "break-word" } as any}>{row.title}</div>
            <div style={{ fontSize: FONT.m, lineHeight: "1.4", color: TEXT, wordWrap: "break-word" } as any}>{row.text}</div>
        </div>
    );
    const portal = (ReactDOM as any) && (ReactDOM as any).createPortal;
    return portal ? portal(el, document.body) : el;
};
