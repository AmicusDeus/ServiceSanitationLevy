# Service & Sanitation Levy

A Cities: Skylines II mod that adds a coverage-gated municipal levy: fire, police and disaster services bill occupants by rent, and garbage bills by rent plus a pollution surcharge ("polluter pays"). Only served buildings are charged.

## Features
- **Fire, police & disaster** — each covered building pays a small % of its own monthly rent, per service. A building not covered by a service isn't billed for it.
- **Garbage (sanitation)** — a % of rent plus a pollution surcharge, so dirty industry pays extra. Gated on the city having at least one garbage facility (recycling centres count). Replaces the native garbage fee.
- Adjustable rates from an in-game panel or the Options page; per-building breakdown shown in the building info panel.

## How it works (transparency)
The levy is charged like a property tax. Each in-game day the mod sums each occupied building's occupant rents (`PropertyRenter.m_Rent`, the game's live economy-driven valuation) and, for every service that actually reaches the building (via `ServiceCoverage`), debits that percentage from the occupants' own household/company balances — a real transfer, exactly like the vanilla service fees, never minted. Garbage adds a pollution surcharge from the building's prefab pollution. The city-side credit is folded into the game's own budget as real `TaxResidential` income, so the native budget system moves the money; a Harmony postfix keeps the budget panel from flickering (falls back safely). The native single garbage fee is locked to 0 so there's no double bill. Opt-out: on by default with modest rates; turn it off for exact vanilla behaviour.

## Part of a set
This is one of three mods that replace the older all-in-one **Economy Tweaks**. The others are **Welfare Management** and **Private Schools & Hospitals**. Don't run Economy Tweaks alongside these (they would double-apply).

## Credits
Made with Claude Code, Anthropic's agentic coding tool.

## License
MIT — see [LICENSE](LICENSE).
