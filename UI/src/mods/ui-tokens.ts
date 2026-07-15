// Native CS2 typography tokens, taken from the game's index.css (:root). Using these instead of hardcoded rem
// sizes / literal "white" makes injected UI match the game font exactly AND honour the player's font-scale
// accessibility setting (the game defines every font size as calc(... * var(--fontScale) ...)). The game applies
// NO letter-spacing to its section titles — it uppercases via text-transform on the game font — so we do the same
// rather than tracking caps out by 1px, which is what made our titles read as non-native.

export const FONT = {
    xxs: "var(--fontSizeXXS)",
    xs: "var(--fontSizeXS)",
    s: "var(--fontSizeS)",
    m: "var(--fontSizeM)",
    l: "var(--fontSizeL)",
    xl: "var(--fontSizeXL)",
} as const;

export const TEXT = "var(--textColor)";              // #F0FBFF — the game's primary text colour
export const TEXT_DIM = "rgba(240,251,255,0.85)";    // --textColor at reduced opacity, for secondary text

// Panel header (e.g. "ECONOMY TWEAKS") — the game's panel titles are bold + uppercase, no letter-spacing.
export const panelTitle = {
    fontSize: FONT.m,
    fontWeight: "bold",
    textTransform: "uppercase",
    color: TEXT,
} as const;

// Section sub-header inside a panel (e.g. "PUBLIC SERVICE FEE") — one step down from the panel title.
export const sectionTitle = {
    fontSize: FONT.s,
    fontWeight: "bold",
    textTransform: "uppercase",
    color: TEXT,
} as const;

// Detail/hover-box heading (e.g. the budget hover box) — matches the game's info detail-box headings.
export const detailTitle = {
    fontSize: FONT.m,
    fontWeight: "bold",
    textTransform: "uppercase",
    color: TEXT,
} as const;
