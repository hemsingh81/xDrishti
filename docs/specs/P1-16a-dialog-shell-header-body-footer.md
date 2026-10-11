# P1-16a — Reusable dialog shell: header, body, footer

- **Status:** Approved (owner instruction 2026-10-10: "create model window as component if not created and separate for header, body, and footer and have different color for header and footer"; design defaults below are mine — object to any)
- **Phase / roadmap row:** P1 — Data platform & instruments · UI refinement of P1-16 (Accounts wizard), applies to every dialog
- **Design refs:** docs/design/13-reports-and-ui.md (React screens); prototype `docs/prototype/assets/proto.css` (`.modal`, `.dlg-h`, `.dlg-b`, `.dlg-f`) and `app-core.js` (modal builder)

## Analysis
Four dialogs exist — `AddAccountWizard`, `RiskProfileDialog`, `StepUpDialog`, `ConfirmDialog` — and each assembles MUI `Dialog` + `DialogTitle` +
`DialogContent` + `DialogActions` itself, with its own padding, focus handling and `<form>` wrapping. The header and footer have the same
background as the body, so the title and the action buttons do not read as separate bands, and there is no close (×) button. The prototype's
modal has a distinct header (`.dlg-h`: title + close button, bottom border, sticky), body (`.dlg-b`: padded) and footer (`.dlg-f`: right-aligned
actions, top border, sticky). There is no shared component, so every new dialog would copy this again (Reuse rule).

## Goal
Every dialog in the app uses one shared shell with a visibly distinct header and footer, a close button, and consistent spacing — matching the
prototype — so new dialogs are built from parts instead of copied.

## Scope
**In:** shared dialog components in `frontend/src/shared/ui/dialog/` (`AppDialog`, `AppDialogHeader`, `AppDialogBody`, `AppDialogFooter`);
dialog band colours in the theme (from existing tokens); migrate all four dialogs; tests.
**Out:** drawers, toasts, the command palette; changing any dialog's content, wording or behaviour.

## Design decisions (defaults — object to any)
1. **Colours from tokens, through the theme:** header background `accentSoft` (light `#eef2ff`, dark `#272a4a`) — a soft brand tint so the title band is
   clearly separate; footer background `surfaceAlt` (light `#f8fafc`, dark `#1a2230`) — a neutral band for the actions; body stays `surface`.
   Header has a bottom border, footer a top border (`border` token). No hex in components — the values live in `theme/tokens.ts` and reach the
   components via the MUI theme (`MuiDialogTitle` / `MuiDialogActions` overrides, so any MUI dialog gets the same bands).
2. **Header** = title (h2 style, the dialog's accessible name via `aria-labelledby`), optional subtitle, close (×) button labelled "Close" that is
   disabled while the dialog is busy. **Body** = scrollable padded content. **Footer** = actions; header and footer stay visible while the body scrolls.
3. **Composition API:** `<AppDialog>` owns open/close, `busy` (blocks Escape/backdrop/× while saving), size, full screen on phones (opt-in),
   initial focus and an optional `onSubmit` that wraps header/body/footer in a `<form>` (Enter submits). Header/footer read `onClose`, `busy` and
   the title id from a small context, so callers do not repeat them.
4. Imported by path (`@/shared/ui/dialog`), not through the `@/shared/ui` barrel, so the initial bundle does not grow (existing rule).

## Acceptance criteria
1. `AppDialog`, `AppDialogHeader`, `AppDialogBody`, `AppDialogFooter` exist under `shared/ui/dialog/` (one component per file, ≤ 250 lines), with a public `index.ts`.
2. The header and footer backgrounds differ from the body (and from each other) in light and dark mode; all colours come from `theme/tokens.ts`.
3. The dialog's accessible name is its title; the × button ("Close") closes it; Escape/backdrop/× do nothing while `busy`.
4. With `onSubmit`, pressing Enter in a field or the submit button submits; without it, no `<form>` is rendered.
5. `AddAccountWizard`, `RiskProfileDialog`, `StepUpDialog` and `ConfirmDialog` use the shell; their content, labels, focus rules (Cancel first
   in confirm, password field in step-up, name field in risk profile) and behaviour are unchanged; existing tests stay green.
6. Usable at 360 px (wizard full screen on phones) and 4K; keyboard accessible; initial bundle still ≤ 200 KB gzip.

## Contract
- **UI only:** no API or data change.

## Non-functional
Accessibility: title is the dialog name, × has an accessible label, focus visible, colour is never the only signal. Performance: no new dependency.

## Test plan
Vitest + Testing Library: shell tests (name from title, × closes, busy blocks close, form submit on Enter, no form without `onSubmit`, footer/header
render their children); existing dialog tests (wizard, risk profiles, step-up, confirm/remove) unchanged and green. Live: wizard in the browser, light and dark.

## Open questions
None — colour choices are defaults above.

## Done when
- [ ] All acceptance criteria have evidence (browser check pending)
- [x] `scripts/check.sh` green; reviewer agent: no blockers/majors (2026-10-10; minors fixed: backdrop + per-dialog focus tests, subtitle contrast, no border when full screen)
- [ ] Deployed (✅ `deploy/xd-up.sh`) and checked in the browser (light + dark) — pending
