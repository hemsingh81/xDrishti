# P2-12 — Match the prototype: padding, margins, fonts, colours

- **Status:** Done (owner, 2026-10-14: "check the prototype in the sense of padding, margin, font name, color that is looking more fine, so based on this update UI here also")
- **Phase / roadmap row:** P2 — UI polish; follows [P1-19](P1-19-ui-foundation-typography-and-colour-system.md), [P1-20](P1-20-colourful-icon-system-sync-with-prototype.md), [P1-21](P1-21-top-bar-search-notifications-and-avatar.md)
- **Design refs:** `docs/prototype/assets/proto.css` (+ `app-core.js` for markup), `frontend/src/theme/*`

## Goal
The React app's spacing, type and colour feel the same as the prototype the owner approved: same page/card padding, gaps, control heights, radii, font sizes/weights, and the prototype's login screen.

## Analysis (what is known before implementation)
Palette and font families already match (tokens copy `proto.css` variables; Inter + JetBrains Mono). Verified gaps so far: the **login page** is a bare centred card, while the prototype has a two-panel layout (hero with brand, headline, feature list, status line on the left; sign-in form on the right) over a dotted background. Everything else is to be **measured**: the implementing agent diffs `proto.css` rules (`.top`, `.side`, `.nav`, `.page-head`, `.card`, `.card-h/.card-b`, `.tbl`, `.btn`, `.input`, `.field`, `.chip`, `.tile`, `.dlg-*`, `.drawer`, `.seg`/tabs, `.banner`, `.empty`, `.kv`) against the MUI theme overrides and the page shell (`AppShell`, `TopBar`, `Sidebar`, `PageHeader`, `Card` wrappers), and records each difference with the prototype value.

## Scope
**In:** `frontend/src/theme/*` (tokens, component overrides), shell layout (`app/layout/*`), shared UI (`shared/ui/*`: PageHeader, cards, dialogs, empty/error states), the login page, and any feature screen whose own `sx` deviates from the shared values. Values go into `tokens.ts`/theme overrides, not hard-coded per screen. Light and dark.
**Out:** new features, data changes, icons (done in P1-20), backend.

## Acceptance criteria
1. A written audit (table in this spec, "Audit result") lists each compared element, prototype value, previous value, and what changed or why it was left.
2. Page content padding, page-head spacing and title/description type, card header/body padding and radius, control heights (buttons 36 / small 30, inputs 36), table cell padding and header style, chips, dialogs/drawers, tabs and banners follow the prototype values (within MUI constraints), in light and dark.
3. Login page has the prototype's two-panel layout (hero left, form right; collapses to the form card below 860 px), dotted background, brand row, feature list and the "Personal use · single owner" footer with theme toggle; fields labelled "Email" and "Password"; existing behaviours (errors, locked countdown, expired notice, `next`) unchanged. No fake status line (no fabricated "12/12 services healthy").
4. No hard-coded hex in components (existing rule); existing tests updated only where markup changed; new tests for the login layout and any new tokens; `contrast.test.ts` still green.
5. Visual verification in the browser at 1440 px, 768 px and 360 px, light and dark, for the login page (can be checked unauthenticated); the signed-in screens are verified by the owner or by measured computed styles (no horizontal scroll). `scripts/check.sh` green; reviewer: no blockers/majors.

## Audit result
Measured with headless Chrome against the prototype (`getComputedStyle` of the signed-in prototype at 1440 px, light and dark), against `proto.css`, and against the React app's theme and rendered output (a throw-away harness page that rendered the real shell, page header, tiles, banners, chips, table and dialog; removed afterwards). Palette hex values and the font families already matched; **two root causes made the app look heavier than the prototype**, and the rest were per-component values.

**Root causes (found while measuring)**

| # | What | Prototype | Before | Now |
|---|------|-----------|--------|-----|
| R1 | Root font size | body 14 px | `html` stayed at the browser's 16 px while the type scale was written in "rem on a 14 px base": body 16, secondary 14.9, caption 13.7, inputs 16, h3 15.2 — everything ~14 % larger | `html { font-size: 87.5% }` (14 px), `htmlFontSize: 14`, scale in px / 14 (`rootFontSize`, `rem()` in `tokens.ts`); literal `rem` sizes in four features rewritten with `rem(px)` |
| R2 | Secondary text colour | muted `--text-2` | MUI 9 no longer reads dotted paths in `Typography color="text.secondary"` (255 uses): captions, descriptions and hints rendered in the primary text colour | `MuiTypography` variants map `text.primary / secondary / tertiary / disabled`, `primary.main`, `error.main`, `success.main`, `warning.main`, `info.main` (tested) |

**Element audit** (prototype value · previous · now / why left)

| Element | Prototype | Previous | Now |
|---------|-----------|----------|-----|
| Page content padding | 24; 14 below 860; 28/36 from 1680; 32/48 from 2560 | 24 (16 on phones), no wide step | `pagePadding` tokens in `AppShell` (14 / 24 / 28·36 / 32·48). **Left:** the prototype's `max-width: 1560` — the owner chose full-width screens earlier (AppShell test) |
| Top bar | 60 px, padding 0 24 (12), gap 10, surface 88 % + 10 px blur, title 16/650 | 64 px + 1, solid surface, title 16 px by accident of R1 | `layout.topBarHeight` 60 including the border, same padding/gap, frosted `MuiAppBar`, explicit 16/650 title |
| Search button | 34 px, radius 8, `surface-2` fill, `text-3` | 36 px, background fill, text-2 | 34 / 8 / `background.alt` / `text.tertiary` |
| Icon buttons | 36 square, radius 10 (30, 8) | 40 circle | `MuiIconButton`: padding 6, radius 10; small 5 / 8 |
| Sidebar width, padding | 248; 16 / 12 | same | unchanged |
| Brand mark | 30 px, radius 9 (login 42, 12) | 32 px, `borderRadius: 2` = 24 px (a circle) | 30 px, radius 30 % of the size |
| Sidebar section label | 10.5 / 700, .09em, padding 14 10 5, `text-3` | 11, .08em, line-height 2.4, text-2 | `Sidebar`: same as the prototype |
| Sidebar link | padding 6 8, radius 11, gap 11, 14/550 (active 600), 1 px apart, hover `surface-2`, active marker flush to the sidebar edge (3 px, radius 0 3 3 0, 8 px inset) | dense list (13 px text), radius 12, 2 px apart, hover accent tint, marker inside the link | `NavRow` + `MuiListItemButton` |
| Page head | gap 14, margin-bottom 18; h1 24/750 (20 below 860, 19 below 480), line height 1.5; actions gap 10 | gap 16, margin-bottom 24 (16 dense), h1 24/700 | `PageHeader`: 14 / 18 (14 dense), 750, responsive h1, gap 10 |
| Card | radius 12, border, shadow `--shadow`; header 13/16 + hairline, title 14.5/650; body 14/16 | no shadow; body 16 (24 below the last child) | `MuiCard` shadow `elevation.md`; `MuiCardContent` 14/16; `MuiCardHeader` 13/16 + hairline, 14.5/650 |
| Stat tile | padding 14/16; label 12/600 text-2 (40 px right padding when it has an icon); value 22/750, -.02em; hint 12; icon chip 34 at 12/12 | label 13, value 22.4/700, hint 13 | `StatTile` |
| Button | 36 (small 30, large 44), radius 10 (8), 14/550 (12.5 small, 15 large), padding 14 / 10, shadow-sm; neutral default has a hairline border and hovers to `surface-3` | 40 / 32 / auto, radius 8, weight 600 | `MuiButton` sizes, radius, weight, shadow, neutral outlined hover (`controlHeight` 30 / 36 / 44) |
| Text field | 36 px, radius 10, 14 px, border `inputBorder`, accent hover and focus ring | 56 px (medium, 16 px text) or 37 px (small) | every field 36 px, `small` by default (`MuiFormControl` / `MuiTextField` defaults), vertical padding 8, floating label centred. **Left:** horizontal padding stays 14 (prototype 11) so the floating label and the text keep one left edge |
| Chip | pill (999), 11.5/600, padding 2/9, tonal fill (soft tint + coloured text; neutral = `surface-3` + text-2) | radius 8, outlined or solid, 24 / 32 px | `MuiChip`: pill, 22 px (small) / 26 px, tonal for `outlined` **and** `filled` (the app uses `outlined` almost everywhere, so one rule gives the prototype look) |
| Table | header 11.5/650, uppercase, .02em, text-2 on `surface-2`; cells 9/12; row hover 5 % accent; alternate rows `surface-2` at 55 % | header 12/600 mixed case, cells 6/16, hover 6 %, no zebra | `MuiTableCell`, `MuiTableRow` (and the Grid's sticky-cell tint) |
| Tabs | 12/12 padding (44 px), 2 px indicator, radius 2 | 48 px, 16 padding, 3 px | `MuiTabs` / `MuiTab`. **Left:** the indicator's 10 px inset (MUI positions it from measurements) |
| Segmented control (`.seg`) | tray `surface-2`, border, radius 10, padding 3, gap 2; segment radius 7, 5/12, 14/600; selected on `surface`, accent text, shadow + ring | stock outlined toggle buttons | `MuiToggleButtonGroup` / `MuiToggleButton` |
| Banner (`.banner`) | padding 11/15, radius 12, 13 px, soft tint + 22–25 % hairline of the colour, text colour = normal text | radius 8, MUI's computed tint, 14 px | `MuiAlert` (icon 18 px) with the new soft tokens |
| Dialog | radius 18, shadow-lg; header 15/20, body 18/20, footer 13/20, hairlines; header and footer on `surface`; title 16.5/700 | header on `accentSoft`, footer on `surfaceAlt` (the P1-16a decision) | surface header and footer with hairlines (paddings and radius were already right); `h2` token 16.5/700. The tinted header is retired to follow the prototype |
| Menu / popover | shadow-lg, radius 12; items radius 8, padding 6 around | shadow md, square items | shadow `lg`, list padding 6, item radius 8 |
| Progress bar | 8 px, radius 4, `surface-3` track | 4 px | `MuiLinearProgress` |
| Elevation | sm `0 1px 2px .04`; base `0 1px 3px .06, 0 1px 2px .03`; lg `0 20px 40px -12px .18` (dark .3 / .3 / .55) | similar but different | `elevation` tokens copy the prototype |
| New tokens | `--text-3`, `--surface-3`, `--border-strong`, `--good-soft`, `--bad-soft`, `--warn-soft`, `--cyan-soft` | missing | `textTertiary`, `surfaceStrong`, `borderStrong`, `goodSoft`, `badSoft`, `warnSoft`, `infoSoft`; MUI gets `text.tertiary`, `background.alt`, `background.strong`; `contrast.test.ts` covers every new pair (4.5:1) |
| Scrollbars / selection | 10 px thin scrollbars in `border-strong`, soft accent selection | browser default | `globalStyles` |
| Login | two panels (see AC 3) | one centred card | rebuilt: `LoginPage`, `LoginHero`, `LoginField`, `LoginFooter` |
| Colours (palette) | — | identical hex values | unchanged |
| **Not adopted** (structural, outside "within MUI constraints"): icon-only sidebar rail 861–1200 px and the collapse toggle; tile / card hover lift; 15 px native checkbox and 34×20 switch (MUI's own control sizes); `.input` labels above every field (only the login form has them); toast colours. | | | |


## Done when
- [ ] All acceptance criteria have evidence · [ ] `scripts/check.sh` green; reviewer approved · [ ] Docs (13 reports-and-ui) mention the audit
