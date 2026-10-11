# P1-19 — UI foundation: professional typography and colour system

- **Status:** Approved (owner, 2026-10-11: "write a new story for UI — best font, colour combinations … make it professional … use your best skills and complete this"; the design choices below are mine — object to any)
- **Phase / roadmap row:** P1 — React shell (cross-cutting, applies to every screen)
- **Design refs:** docs/design/13-reports-and-ui.md; prototype `docs/prototype/assets/proto.css`; inputs: Figma "website colour schemes" guide (neutral base + one sparing accent, cool/monochrome schemes for trust, contrast for hierarchy) and a WCAG 2.2 AA audit of the current tokens

## Audit of the current system (computed contrast ratios)
| Finding | Today | Why it matters |
|---|---|---|
| Success green as text on white (light) | `#16a34a` → **3.3:1** (needs 4.5) | gains, "Connected", success toasts fail AA |
| Info cyan as text on white (light) | `#0891b2` → **3.7:1** | info chips/links fail AA |
| Secondary text on the tinted dialog header (light) | `#6b7280` on `#eef2ff` → **4.3:1** | subtitles/captions fail AA |
| Primary button label in dark mode | white on `#818cf8` → **3.0:1** | every dark-mode primary button fails AA |
| Accent in light mode | `#4f46e5` 6.3:1 — passes, but bright "default Tailwind indigo" reads generic | no brand character; same hue as links, buttons, selection, charts |
| Neutrals | grey-blue surfaces with `#e5e7eb` borders, `#1f2937` text | acceptable, but low separation between background / surface / header bands, and text could be crisper |
| Numbers | proportional figures | columns of prices/quantities do not align; in a trading app this reads as unprofessional |
| Type scale | h1 24 / h2 18 / h3 15 / body 14 only; no caption / overline / numeric roles; mono is a bare system stack | inconsistent small text, no defined hierarchy for data |
| Tokens | colours + radii + 2 fonts only | no elevation, focus, motion or spacing tokens; shadows and focus rings are MUI defaults |

## Goal
A calm, trustworthy, data-first look: crisp neutral base, one deliberate accent, accessible semantic colours, a typographic scale built for dense financial data with aligned numbers — all defined once as tokens and applied through the MUI theme, in light and dark.

## Design decisions (defaults — object to any)
**Colour (60-30-10):** 60 % cool-neutral backgrounds, 30 % surfaces and text, ≤ 10 % accent. Cool slate neutrals + a deeper indigo accent signal stability for a finance product; the accent is reserved for primary actions, links, selection and focus — never for gain/loss.
| Token | Light | Dark | Role |
|---|---|---|---|
| background | `#f4f6fa` | `#0b1220` | app canvas (60 %) |
| surface | `#ffffff` | `#131c2e` | cards, dialogs, tables |
| surfaceAlt | `#f1f4f9` | `#182238` | dialog footer, table header, hover rows |
| border | `#dfe3ea` | `#26324a` | hairlines (cards, table rows, dividers) |
| inputBorder | `#7b8798` | `#5f6f8a` | input outlines, unchecked checkbox/radio, switch track (3:1+ on surface, surfaceAlt and background) |
| text | `#0f172a` | `#e6eaf2` | primary text |
| textSecondary | `#475569` | `#9aa7bd` | captions, helper text |
| accent | `#4338ca` | `#9aa5ff` | primary actions, links, selection |
| accentContrast | `#ffffff` | `#0b1220` | text on accent fills (**new** — fixes the dark button) |
| accentSoft | `#eceefe` | `#222a52` | dialog header band, selected rows, info banners |
| good | `#146c36` | `#4ade80` | gains, connected, success |
| bad | `#b91c1c` | `#f87171` | losses, errors |
| warn | `#a35200` | `#fbbf24` | warnings, expiring |
| info | `#0e7490` | `#67e8f9` | neutral information |
Measured contrast of the proposal: text 17.9 / 14.1, textSecondary ≥ 6.6 on every surface (7.6 on white), accent 7.9 on white / 7.5 on dark surface, accentContrast on accent 7.9 (white on `#4338ca`) and 8.2 (`#0b1220` on `#9aa5ff`), good 6.5 (5.7 on accentSoft), bad 6.5, input border 3.3-3.7 on every surface, warn 5.6, info 5.4 on white; dark semantic colours 6.2–11.7 on the dark surface. Gain/loss are never signalled by colour alone (▲/▼ or +/− and text).
**Typography:** keep **Inter Variable** (designed for screens, large x-height, true tabular figures, slashed zero — the standard choice for dense dashboards), self-hosted through `@fontsource-variable/inter` (no CDN; the stack is local-only). Add **JetBrains Mono Variable** (`@fontsource-variable/jetbrains-mono`, exact version pinned) for ids, URLs and code, replacing the system mono stack. Scale on a 14 px base: h1 24/700, h2 18/650, h3 15/650, body1 14/1.5, body2 13/1.45, caption 12/1.4, overline 11/600 uppercase +0.06em, button 14/600, numeric roles use `font-variant-numeric: tabular-nums` (+ `font-feature-settings: 'cv11', 'zero'`): tables, stat tiles, chips with numbers, countdowns, header status. Headings tighten letter-spacing (−0.01 to −0.02em); body stays at 0.
**Other tokens (new):** elevation (`sm`, `md`, `lg`; softer, cool-tinted in light, border-led in dark), focus ring (2 px accent outline, 2 px offset, visible on every interactive element), motion (`fast` 120 ms, `base` 200 ms, easing; honour `prefers-reduced-motion`), spacing base 4 px, control height (small 32 / medium 40), table density (compact rows 36 px, header on `surfaceAlt`, hover row `accentSoft` at low strength), sidebar (selected item on `accentSoft` with a 3 px accent marker; icons `textSecondary`).

## Acceptance criteria
1. `theme/tokens.ts` holds the palettes above (plus `accentContrast`), type scale, `monoFontFamily`, elevation, focus, motion and spacing/control tokens; `theme/theme.ts` maps them into the MUI theme (palette incl. `primary.contrastText`, `text`, `divider`, `action` states, typography variants, shadows, component overrides for Button, TextField/Input, Chip, Tabs, Table, Card, Dialog bands, Tooltip, Popover, Alert, Link, Switch/Checkbox focus). No hard-coded hex remains in components other than user-chosen account colours.
2. Contrast: a unit test computes WCAG ratios for the documented foreground/background pairs of **both** palettes and fails below 4.5:1 for text pairs and 3:1 for UI-component pairs (borders against surface, focus ring, filled accent); the audit failures listed above are gone.
3. Typography: Inter Variable and JetBrains Mono Variable load self-hosted (preloaded, `font-display: swap`); numeric roles use tabular figures — verified by a test that the theme's `numeric` style/class sets `tabular-nums` and by use on StatTile, ConnectionStatus countdown, tables and header account counts; the redirect URL / ids use the mono token.
4. Components look consistent: primary/secondary/text buttons, inputs (rest, hover, focus, error, disabled), chips, tabs, tables (header, row hover, selected), dialogs (header/body/footer bands from P1-16a), alerts, popover/menus, sidebar and top bar all use the new tokens; dark mode has parity (no white-on-pastel, no pure-black or pure-white fields).
5. Accessibility: visible focus on all interactive elements (keyboard walk of login, shell, Accounts, wizard, header menu); `prefers-reduced-motion` disables non-essential transitions; status is never colour-only; works at 360 px and 4K without horizontal page scroll.
6. Performance: initial bundle still ≤ 200 KB gzip; fonts add no more than the two variable fonts' latin subsets and are not render-blocking; only the weights/axes used are loaded.
7. The clickable prototype (`docs/prototype/assets/proto.css`) is updated to the same tokens so the reference matches the app; design doc 13 gets a short "Visual system" section with the token tables and rules; a delivery-plan story P1-19 exists.
8. Tests: token/contrast tests, theme tests (variants, overrides present), existing suites green; live check in light and dark on login, shell, Accounts, wizard and the header menu with before/after screenshots.

## Contract
UI only — no API or data change.

## Non-functional
No new runtime dependency except the pinned `@fontsource-variable/jetbrains-mono`; no CDN; no layout shift from font loading (size-adjusted fallback stack).

## Open questions
None blocking. Brand logo/name styling and chart categorical colours are separate stories (charts arrive in P2).

## Done when
- [ ] All acceptance criteria have evidence (signed-in screens checked by the owner pending) · [x] `scripts/check.sh` green; reviewer: majors/minors fixed (2026-10-11) · [x] Deployed; login page checked in light + dark · [x] Doc 13 and prototype updated
