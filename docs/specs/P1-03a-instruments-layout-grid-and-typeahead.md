# P1-03a — Instruments screen polish: full-width layout, on-demand refresh panel, scrollable reusable grid, type-ahead control

- **Status:** Approved (owner, 2026-10-11, after trying P1-03: "use full available space · refresh panel hidden by default, shown when I press refresh · scrollable instrument list · type-ahead dropdown control for sector · reusable grid control for symbol listings")
- **Phase / roadmap row:** P1 — Instruments screen (UI refinement of [P1-03](P1-03-instrument-master-import-search-and-refresh.md); the grid is reused by P1-05, P1-15 and later list screens)

## Analysis (from the owner's screenshot)
1. The app content is capped to a max width, leaving a blank band on the right of wide screens.
2. The "Master list" refresh card is always open and pushes the list down; it is only interesting while refreshing or when something went wrong.
3. The list grows the whole page (page scrolls, header and filters scroll away); the owner wants the list itself to scroll.
4. Sector is a plain select; with ~22 sectors plus counts a searchable type-ahead is faster, and the same control will serve other long lists (baskets, accounts, series).
5. The table is hand-built inside the feature; P1-05 (selection, bulk actions), P1-15 (basket members, drag & drop) and the later tracked/audit lists need the same behaviour → one reusable grid.

## Design (defaults — object to any)
- **Layout:** the shell content area fills the available width (padding only, no max-width) on every page; text-heavy pages keep readable line lengths through their own inner widths, not the shell. The Instruments page becomes a flex column filling the viewport height under the top bar: header, toolbar (search + filters) fixed, **grid body scrolls**, pagination footer fixed at the bottom; below `md` the page scrolls normally (grid keeps its own horizontal scroll).
- **Refresh:** the page header gets a compact line "Last refreshed 6 min ago · 4,381 active" and a **Refresh master list** split button (Refresh · Force full refresh). The detailed panel (progress, result summary, warnings, details, schedule line) is **hidden by default**; it opens when the owner presses Refresh (which starts the run) or clicks the last-refresh line / a "Last refresh failed / has warnings" chip, shows live progress, stays after finishing so the result can be read, and can be dismissed (×). It never opens by itself on page load, except that a run already in progress (started by the daily service or another tab) shows a small "Refreshing…" chip that opens it.
- **Reusable grid (`shared/ui/grid`, no heavy dependency, built on MUI Table + CSS grid sticky parts):** props: `columns` (id, header, accessor/render, width/min/flex, align, numeric → tabular figures, sortable, hidden below breakpoint), `rows`, `getRowId`, controlled `sort`/`onSortChange` (aria-sort), optional controlled **row selection** (checkbox column, select-all-on-page, shift-click ranges, `selectedIds`), `onRowClick`, optional **pagination** footer (page, pageSize options, total, "1–50 of 3,204"), `loading` (skeleton rows, keeps previous rows dimmed), `error` + retry, `emptyState`, `stickyHeader`, `density` (compact/comfortable), `height` behaviour (fills parent; the body is the scroll container), keyboard navigation (arrow keys between rows, Enter opens, Space toggles selection), accessible roles (`grid`/`row`/`columnheader`/`gridcell` or table semantics with labels), horizontal scroll with sticky first column option, responsive. Instruments table is rebuilt on it; the old feature-local table code is deleted.
- **Type-ahead control (`shared/ui/Typeahead`, MUI Autocomplete underneath):** props: options (value, label, optional count/hint), value, onChange, label, placeholder, clearable, `freeSolo=false`, loading, no-options text, `getOptionDisabled`; filters as you type (case-insensitive substring, highlights match), shows counts right-aligned, keyboard + screen reader semantics (combobox/listbox), clear button, fits the filter bar. Used for **Sector**; Series stays a compact select unless trivial to switch.

## Acceptance criteria
1. At 1280, 1920 and 2560 px the content uses the full width (no right-hand blank band) on Instruments, Accounts, Settings and System status; at 360 px nothing overflows horizontally.
2. Instruments: header/toolbar stay in view while the list scrolls inside its own region with sticky column headers and a fixed pagination footer; page itself does not scroll on ≥ `md` screens.
3. The refresh panel is not in the DOM until the owner presses Refresh / the last-refresh line / a status chip; pressing Refresh starts a run (409 handled), shows progress then the result, and can be dismissed; the compact header line and chip still show last time, result and any warning/failure; a run already in progress shows the chip.
4. `Grid` component exists in `shared/ui/grid` with the props above, documented in its file header and in doc 13; the Instruments list uses it; keyboard navigation, aria-sort, selection and pagination work and are unit-tested; no duplicated table code remains.
5. `Typeahead` exists in `shared/ui`, is used for Sector (counts shown, Unclassified included), tested for filtering, keyboard, clearing, no-options and a11y; URL state for sector keeps working.
6. Existing Instruments behaviour (search, filters, URL state, detail drawer, refresh flows) unchanged and tested; initial bundle ≤ 200 KB gzip (Grid/Typeahead are imported by lazy features; keep them out of the barrel if they pull MUI Autocomplete/Table into the initial chunk); `scripts/check.sh` green; reviewer no blockers/majors; deployed and checked in the browser.

## Test plan
Vitest: Grid (render, sort, select incl. shift-range, keyboard, pagination, loading/empty/error, sticky/scroll structure, responsive column hiding), Typeahead (filter, highlight, counts, select/clear, keyboard, no options), Instruments page (refresh panel hidden by default, opens on press, in-progress chip, dismiss, failure chip), layout tests where feasible.

## Done when
- [ ] All acceptance criteria have evidence · [ ] `scripts/check.sh` green; reviewer: no blockers/majors · [ ] Deployed and checked in the browser
