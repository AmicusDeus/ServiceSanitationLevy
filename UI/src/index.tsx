import { ModRegistrar, getModule } from "cs2/modding";
import { Safe } from "mods/budget-section";
import { LevyButton, LevyPanelHost, LevyInfoRow, BudgetDetailInject, BudgetRowHoverLayer } from "mods/levy";

const register: ModRegistrar = (moduleRegistry) => {
    console.info("[ServiceSanitationLevy] register() running");

    // Budget DETAIL breakdown: the collected levy is folded into the Taxes total, so we show a "Safety & Sanitation
    // Levy" sub-item in the Taxes line's detail box (hover the line), netted out of the Residential slot.
    try {
        const BUDGET_ITEM_DETAIL = "game-ui/game/components/economy-panel/budget-page/budget-item-detail/budget-item-detail.tsx";
        moduleRegistry.extend(BUDGET_ITEM_DETAIL, "BudgetItemDetail", (Original: any) => (props: any) => (
            <Safe><BudgetDetailInject Original={Original} detailProps={props} /></Safe>
        ));
    } catch (e) { console.info("[SSL] extend(BudgetItemDetail) error: " + String(e)); }

    // Per-building info: the Public Service Fee breakdown line in the selected building's Employees (companies) and
    // Residents (residential) sections. Self-hides unless the levy is on and the building is chargeable.
    try {
        const SECTIONS = "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx";
        const map: any = getModule(SECTIONS, "selectedInfoSectionComponents");
        const wrap = (typeName: string) => {
            const Orig = map[typeName];
            map[typeName] = (props: any) => (
                <>
                    {Orig ? <Orig {...props} /> : null}
                    <Safe><LevyInfoRow /></Safe>
                </>
            );
        };
        wrap("Game.UI.InGame.EmployeesSection");
        wrap("Game.UI.InGame.ResidentsSection");
    } catch (e) { console.info("[SSL] section map wrap error: " + String(e)); }

    // Toolbar button + floating panel + budget-row hover layer.
    try {
        moduleRegistry.append("GameTopRight", LevyButton);
        moduleRegistry.append("Game", LevyPanelHost);
        moduleRegistry.append("Game", BudgetRowHoverLayer);
    } catch (e) { console.info("[SSL] panel error: " + String(e)); }
};

export default register;
