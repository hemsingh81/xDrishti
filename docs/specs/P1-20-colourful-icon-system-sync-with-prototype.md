# P1-20 — Colourful icon system (sync the app with the prototype)

- **Status:** Approved (owner, 2026-10-11: "icons are not colourful — check the prototype's design and sync with that")
- **Phase / roadmap row:** P1 — React shell (cross-cutting); follows [P1-19](P1-19-ui-foundation-typography-and-colour-system.md)
- **Design refs:** prototype `docs/prototype/assets/proto.css` (`.nav-ic`, `.h-ico`, `.tile .t-ico`, `.lesson .l-ico`, `.toast .ic`) and `ui.js` (`NAVSTYLE`, tile colour table `C`, `TILE_RULES`)

## Analysis
P1-19 gave the app one accent and neutral grey icons. The prototype is more lively: **every icon sits in a rounded chip tinted from its own colour pair** — navigation items, page-header icon beside the title, stat tiles (top right), card headers, lesson cards, toasts. Colour carries meaning at a glance (amber = today/audit, green = review/quality, sky = portfolio/services, …) while chips stay soft (12–16 % tint) so the page remains calm and the accent still means "action". The app currently shows plain grey glyphs, so the screens read flatter than the reference.

## Goal
Icons in the app look like the prototype: coloured chips per navigation item, per stat tile, per card/page header, defined once as tokens, in light and dark, accessible (icons always have a text label next to them; chips never carry meaning alone).

## Design (from the prototype — keep the hues, adjust only for contrast)
- **Chip anatomy:** rounded square (nav 28–30 px/radius 8, header 26 px/radius 8, tile 34 px/radius 10, large 40 px/radius 12), icon 17 px (tile 17, header 15), background = colour pair's strong value at 12 % (tile 11 %, header 18 % of the light value; dark mode 15–16 % of the light value), icon colour = strong value in light mode, light value in dark mode.
- **Colour pairs (light-value `c1`, strong `c2`)** from the prototype `NAVSTYLE`: today amber/orange `#f59e0b`/`#f97316`, review green `#10b981`/`#059669`, suggestions fuchsia/violet `#d946ef`/`#8b5cf6`, portfolio sky/blue `#38bdf8`/`#3b82f6`, instruments indigo `#818cf8`/`#4f46e5`, baskets orange/rose `#fb923c`/`#f43f5e`, data cyan/teal `#22d3ee`/`#0d9488`, quality lime/green `#a3e635`/`#16a34a`, reports violet `#c4b5fd`/`#7c3aed`, lab pink `#f472b6`/`#db2777`, learning teal/indigo `#2dd4bf`/`#6366f1`, accounts blue `#60a5fa`/`#2563eb`, services sky/indigo `#38bdf8`/`#6366f1`, settings zinc `#a1a1aa`/`#52525b`, audit amber `#fbbf24`/`#b45309`, system status teal (new: `#2dd4bf`/`#0f766e`), fallback slate `#94a3b8`/`#64748b`; **tile/header hues** `C`: indigo, green, amber, rose, sky, violet, teal, pink, slate (values in `ui.js`), keyword-to-hue rules for tiles (money → indigo/amber, gains/ok → green, loss/risk → rose, data/import → teal, time/day → sky, learning/model → teal, automation → pink, history → slate).
- **Contrast:** icons are decorative next to text, but meaningful graphics should reach 3:1 where feasible: compute each light-mode icon colour on its own chip background (and dark-mode light value on its dark chip); where a hue falls below 3:1 (yellow/amber/lime/orange/cyan are the likely ones), darken that single value minimally and keep the hue — list the adjusted values in the token file comment and in doc 13.
- **Semantics:** accent stays for actions/selection; gain/loss stay green/red with ▲▼/+− signs; chips never replace labels or status text.

## Acceptance criteria
1. `theme/tokens.ts` has an `iconHues` table (name → {light, strong} pairs per the list above, contrast-adjusted) and a `navHue` map (route key → hue); no hex in components.
2. A reusable `IconChip` component in `shared/ui` (props: icon, hue, size `sm|md|lg`, optional `label` for assistive text when it stands alone) renders the chip per the anatomy in both modes using only theme values (the hue is passed as a token name, resolved through the theme).
3. Sidebar items use `IconChip` with their own hue (selected item keeps the P1-19 `accentSoft` row + accent marker; chip stays coloured); the planned "coming soon" items too; the brand mark unchanged.
4. `PageHeader` shows an optional icon chip before the title (every existing page passes its nav icon/hue: Accounts, Settings, System status, Today, coming-soon pages); `StatTile` gets an optional icon chip top-right (System status, Accounts overview tiles get hues from the keyword rules); card/section headers on Accounts (broker connection, broker app, risk profile cards, header accounts menu rows where an icon fits) use small header chips; the add-account wizard steps and dialog headers may show a chip too; toasts use coloured status icons (info/success/error) like the prototype.
5. Contrast test covers every hue pair (3:1 target, never below 2.4 without a recorded reason) in both modes; dark mode parity checked (chips use the light value on a dark tint).
6. Prototype and docs: `docs/design/13-reports-and-ui.md` §1e gains an "Icons" subsection (anatomy, hue table, rules); prototype stays the reference (adjusted values copied back if contrast changed them); story added to the delivery plan.
7. Tests: `IconChip` (renders icon, size/hue → colours via theme, label), Sidebar (each nav item has a chip with its hue), PageHeader/StatTile with icon, existing suites green. Bundle ≤ 200 KB gzip (icons already tree-shaken per import).
8. Live check light + dark on login, shell, Accounts, wizard, System status with screenshots.

## Non-functional
UI only. No new dependency (MUI icons already installed).

## Done when
- [ ] All acceptance criteria have evidence (signed-in screens checked by the owner pending) · [x] `scripts/check.sh` green; reviewer: approved, minors fixed (2026-10-11) · [x] Deployed; signed-in light + dark check pending (needs the owner's login)
