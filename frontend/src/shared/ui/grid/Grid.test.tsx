import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { appliedCss } from '@/test/css';
import { observedCount, observerMargins, scrollToEnd } from '@/test/intersection';
import { AppThemeProvider } from '@/theme';
import { Grid, type GridColumn, type GridInfinite, type GridProps, type GridSort } from '.';

interface Row {
  id: string;
  symbol: string;
  name: string;
  lot: number;
}

const rows: Row[] = [
  { id: 'a', symbol: 'ALPHA', name: 'Alpha Ltd', lot: 1 },
  { id: 'b', symbol: 'BRAVO', name: 'Bravo Ltd', lot: 25 },
  { id: 'c', symbol: 'CHARLIE', name: 'Charlie Ltd', lot: 300 },
  { id: 'd', symbol: 'DELTA', name: 'Delta Ltd', lot: 4 },
  { id: 'e', symbol: 'ECHO', name: 'Echo Ltd', lot: 5 },
];

const columns: GridColumn<Row>[] = [
  { id: 'symbol', header: 'Symbol', sortable: true, accessor: (r) => r.symbol },
  { id: 'name', header: 'Name', grow: true, accessor: (r) => r.name },
  { id: 'lot', header: 'Lot', sortable: true, numeric: true, accessor: (r) => r.lot },
  { id: 'note', header: 'Note', hideBelow: 'md', render: (r) => <em>note {r.id}</em> },
];

type Props = Partial<GridProps<Row>>;

function view(props: Props = {}) {
  return (
    <AppThemeProvider>
      <div style={{ display: 'flex', flexDirection: 'column', height: 400 }}>
        <Grid<Row> label="Things" columns={columns} rows={rows} getRowId={(r) => r.id} {...props} />
      </div>
    </AppThemeProvider>
  );
}

const grid = () => screen.getByRole('grid', { name: 'Things' });
// The rows of data: not the header, nor the status row of an infinite list.
const dataRows = () =>
  within(grid())
    .getAllByRole('row')
    .slice(1)
    .filter((row) => row.hasAttribute('data-grid-row'));
const symbols = () => dataRows().map((r) => within(r).getAllByRole('gridcell')[0]?.textContent);

/** A grid that keeps the sort and the selection in state, as a screen would. */
function Stateful(props: Props) {
  const [sort, setSort] = useState<GridSort | null>(null);
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  return view({
    sort,
    onSortChange: setSort,
    selectedIds: selected,
    onSelectionChange: setSelected,
    getRowLabel: (r) => r.symbol,
    ...props,
  });
}

describe('Grid: rendering', () => {
  it('is a grid with column headers, rows and cells', () => {
    render(view());

    expect(screen.getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Symbol', 'Name', 'Lot', 'Note']);
    expect(symbols()).toEqual(['ALPHA', 'BRAVO', 'CHARLIE', 'DELTA', 'ECHO']);
    const bravo = within(dataRows()[1]!);
    expect(bravo.getByText('Bravo Ltd')).toBeInTheDocument();
    expect(bravo.getByText('note b')).toBeInTheDocument(); // `render` wins over `accessor`
    expect(grid()).toHaveAttribute('aria-busy', 'false');
  });

  it('scrolls inside a labelled region, so a wide grid never widens the page', () => {
    render(view());

    const region = screen.getByRole('region', { name: /scrolls sideways$/ });
    expect(region).toContainElement(grid());
  });

  it('right-aligns numeric columns', () => {
    render(view());

    expect(screen.getByRole('columnheader', { name: 'Lot' })).toHaveStyle({ textAlign: 'right' });
    expect(within(dataRows()[0]!).getByText('1')).toHaveStyle({ textAlign: 'right' });
    expect(screen.getByRole('columnheader', { name: 'Symbol' })).toHaveStyle({ textAlign: 'left' });
  });

  it('hides a column below its breakpoint (header and cells) with a media rule, and the others stay', () => {
    render(view());

    const hidden = (element: Element) => appliedCss(element).responsive.map(({ query, css }) => `${query} ${css}`);
    const expected = ['(min-width:0px) display: none;', '(min-width:900px) display: table-cell;'];
    expect(hidden(screen.getByRole('columnheader', { name: 'Note' }))).toEqual(expected);
    expect(hidden(within(dataRows()[0]!).getAllByRole('gridcell')[3]!)).toEqual(expected);
    expect(hidden(screen.getByRole('columnheader', { name: 'Symbol' }))).toEqual([]);
  });
});

describe('Grid: sorting', () => {
  it('marks only the sorted column with aria-sort', () => {
    render(view({ sort: { columnId: 'symbol', descending: false }, onSortChange: vi.fn() }));
    expect(screen.getByRole('columnheader', { name: 'Symbol' })).toHaveAttribute('aria-sort', 'ascending');
    expect(screen.getByRole('columnheader', { name: 'Lot' })).not.toHaveAttribute('aria-sort');
  });

  it('starts a new column ascending, then flips the direction', async () => {
    render(<Stateful />);
    expect(screen.queryByRole('columnheader', { selected: true })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Lot' }));
    expect(screen.getByRole('columnheader', { name: 'Lot' })).toHaveAttribute('aria-sort', 'ascending');

    await userEvent.click(screen.getByRole('button', { name: 'Lot' }));
    expect(screen.getByRole('columnheader', { name: 'Lot' })).toHaveAttribute('aria-sort', 'descending');

    await userEvent.click(screen.getByRole('button', { name: 'Symbol' }));
    expect(screen.getByRole('columnheader', { name: 'Symbol' })).toHaveAttribute('aria-sort', 'ascending');
    expect(screen.getByRole('columnheader', { name: 'Lot' })).not.toHaveAttribute('aria-sort');
  });

  it('reports the requested sort and leaves the rows to the caller', async () => {
    const onSortChange = vi.fn();
    render(view({ sort: { columnId: 'lot', descending: false }, onSortChange }));

    await userEvent.click(screen.getByRole('button', { name: 'Lot' }));

    expect(onSortChange).toHaveBeenCalledWith({ columnId: 'lot', descending: true });
    expect(symbols()).toEqual(['ALPHA', 'BRAVO', 'CHARLIE', 'DELTA', 'ECHO']); // controlled: not re-sorted by the grid
  });

  it('has no sort buttons on columns that are not sortable, or without a handler', () => {
    render(view({ onSortChange: vi.fn() }));
    expect(screen.queryByRole('button', { name: 'Name' })).not.toBeInTheDocument();
  });
});

describe('Grid: selection', () => {
  it('has no checkboxes unless selection is wired', () => {
    render(view());
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });

  it('ticks rows, and select-all-on-page ticks and clears the page (indeterminate in between)', async () => {
    render(<Stateful />);
    const all = screen.getByRole('checkbox', { name: 'Select all rows' });

    await userEvent.click(screen.getByRole('checkbox', { name: 'Select BRAVO' }));
    expect(dataRows()[1]).toHaveAttribute('aria-selected', 'true');
    expect(dataRows()[0]).toHaveAttribute('aria-selected', 'false');
    expect(all).toHaveAttribute('data-indeterminate', 'true');

    await userEvent.click(all);
    expect(screen.getAllByRole('checkbox', { checked: true })).toHaveLength(6); // five rows + the header
    await userEvent.click(all);
    expect(screen.queryAllByRole('checkbox', { checked: true })).toHaveLength(0);
  });

  it('selects a range with shift-click, and clears a range the same way', async () => {
    const user = userEvent.setup();
    render(<Stateful />);
    await user.click(screen.getByRole('checkbox', { name: 'Select ALPHA' }));
    await user.keyboard('{Shift>}');
    await user.click(screen.getByRole('checkbox', { name: 'Select DELTA' }));
    await user.keyboard('{/Shift}');

    const ticked = () => screen.getAllByRole('checkbox', { checked: true }).map((c) => c.getAttribute('aria-label'));
    expect(ticked()).toEqual(['Select ALPHA', 'Select BRAVO', 'Select CHARLIE', 'Select DELTA']);

    await user.click(screen.getByRole('checkbox', { name: 'Select DELTA' })); // untick: anchor is now DELTA
    await user.keyboard('{Shift>}');
    await user.click(screen.getByRole('checkbox', { name: 'Select BRAVO' }));
    await user.keyboard('{/Shift}');
    expect(ticked()).toEqual(['Select ALPHA']);
  });

  it('keeps selections made on other pages when selecting the page', async () => {
    const onSelectionChange = vi.fn();
    render(view({ selectedIds: new Set(['elsewhere']), onSelectionChange }));

    await userEvent.click(screen.getByRole('checkbox', { name: 'Select all rows' }));

    expect(onSelectionChange).toHaveBeenCalledWith(new Set(['elsewhere', 'a', 'b', 'c', 'd', 'e']));
  });

  it('does not open the row when its checkbox is ticked, and Space toggles the focused row', async () => {
    const onRowClick = vi.fn();
    render(<Stateful onRowClick={onRowClick} />);

    await userEvent.click(screen.getByRole('checkbox', { name: 'Select ALPHA' }));
    expect(onRowClick).not.toHaveBeenCalled();

    dataRows()[2]!.focus();
    await userEvent.keyboard(' ');
    expect(screen.getByRole('checkbox', { name: 'Select CHARLIE' })).toBeChecked();
    await userEvent.keyboard(' ');
    expect(screen.getByRole('checkbox', { name: 'Select CHARLIE' })).not.toBeChecked();
    expect(onRowClick).not.toHaveBeenCalled();
  });
});

describe('Grid: rows and keyboard', () => {
  it('opens a row on click and on Enter', async () => {
    const onRowClick = vi.fn();
    render(view({ onRowClick }));

    await userEvent.click(screen.getByText('Bravo Ltd'));
    expect(onRowClick).toHaveBeenLastCalledWith(rows[1]);

    dataRows()[3]!.focus();
    await userEvent.keyboard('{Enter}');
    expect(onRowClick).toHaveBeenLastCalledWith(rows[3]);
  });

  it('has a single tab stop and moves between rows with the arrow keys, Home and End', async () => {
    render(view({ onRowClick: vi.fn() }));
    expect(dataRows().map((r) => r.tabIndex)).toEqual([0, -1, -1, -1, -1]);

    await userEvent.tab(); // the header has no stops here: no sortable handler
    expect(dataRows()[0]).toHaveFocus();
    await userEvent.keyboard('{ArrowDown}{ArrowDown}');
    expect(dataRows()[2]).toHaveFocus();
    expect(dataRows().map((r) => r.tabIndex)).toEqual([-1, -1, 0, -1, -1]);
    await userEvent.keyboard('{ArrowUp}');
    expect(dataRows()[1]).toHaveFocus();
    await userEvent.keyboard('{End}');
    expect(dataRows()[4]).toHaveFocus();
    await userEvent.keyboard('{ArrowDown}'); // stops at the last row
    expect(dataRows()[4]).toHaveFocus();
    await userEvent.keyboard('{Home}');
    expect(dataRows()[0]).toHaveFocus();
  });

  it('keeps the zebra tint on a row that is not clickable, and tints a clickable row on hover', () => {
    // jsdom has no pointer: read the rules emotion injected for the row's class.
    const rulesFor = (row: Element) => {
      const cls = Array.from(row.classList).find((name) => name.startsWith('css-'))!;
      return Array.from(document.styleSheets)
        .flatMap((sheet) => Array.from(sheet.cssRules))
        .filter((rule): rule is CSSStyleRule => 'selectorText' in rule && String(rule.selectorText).includes(`.${cls}`))
        .map((rule) => `${rule.selectorText} { ${rule.style.cssText} }`);
    };
    const hoverTints = (rules: string[]) =>
      rules.filter(
        (rule) => rule.includes(':hover') && !rule.includes('Mui-selected') && rule.includes('--grid-row-tint:'),
      );
    const { unmount } = render(view());
    const plain = rulesFor(dataRows()[0]!);
    expect(hoverTints(plain)).toEqual([]);
    // One source for the zebra: the row paints the tint variable, also on even rows.
    expect(
      plain.some(
        (rule) => rule.includes('nth-of-type(even)') && rule.includes('background-color: var(--grid-row-tint)'),
      ),
    ).toBe(true);
    unmount();

    render(view({ onRowClick: vi.fn() }));
    expect(hoverTints(rulesFor(dataRows()[0]!))).toHaveLength(1);
  });

  it('marks the row that is open elsewhere', () => {
    render(view({ activeRowId: 'c', onRowClick: vi.fn() }));

    expect(dataRows().map((r) => r.getAttribute('aria-current'))).toEqual([null, null, 'true', null, null]);
  });
});

describe('Grid: infinite scroll', () => {
  const infinite = (over: Partial<GridInfinite> = {}): GridInfinite => ({
    total: 3204,
    hasMore: true,
    loadingMore: false,
    onLoadMore: vi.fn(),
    onRetryMore: vi.fn(),
    ...over,
  });
  const more: Row[] = [
    { id: 'f', symbol: 'FOXTROT', name: 'Foxtrot Ltd', lot: 6 },
    { id: 'g', symbol: 'GOLF', name: 'Golf Ltd', lot: 7 },
  ];

  it('shows "Showing N of total" with digit grouping, politely announced, and no pager', () => {
    render(view({ infinite: infinite() }));

    const status = screen.getByText('Showing 5 of 3,204');
    expect(status).toHaveAttribute('aria-live', 'polite');
    expect(screen.queryByText(/Rows per page/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /next page/i })).not.toBeInTheDocument();
    expect(grid().parentElement!.contains(status)).toBe(false); // the footer is outside the scroll area
  });

  it('says "0 results" for an empty list and shows the same total once everything is loaded', () => {
    const { unmount } = render(view({ rows: [], infinite: infinite({ total: 0, hasMore: false }) }));
    expect(screen.getByText('0 results')).toBeInTheDocument();
    unmount();

    render(view({ infinite: infinite({ total: 5, hasMore: false }) }));
    expect(screen.getByText('Showing 5 of 5')).toBeInTheDocument();
    expect(observedCount()).toBe(0); // nothing left to load: no sentinel is watched
  });

  it('asks for the next page once when the end of the rows nears the viewport', () => {
    const props = infinite();
    render(view({ infinite: props }));
    expect(observerMargins()).toContain('400px 0px');
    expect(props.onLoadMore).not.toHaveBeenCalled();

    scrollToEnd();
    scrollToEnd(); // a second report before the parent has reacted does not ask twice

    expect(props.onLoadMore).toHaveBeenCalledTimes(1);
  });

  it('asks again after the page arrived (a first page that does not fill the viewport keeps loading)', () => {
    const props = infinite();
    const { rerender } = render(view({ infinite: props }));
    scrollToEnd();
    rerender(view({ infinite: { ...props, loadingMore: true } }));
    rerender(view({ infinite: { ...props, loadingMore: false }, rows: [...rows, ...more] }));
    expect(observedCount()).toBe(1); // armed again, and the browser reports a visible sentinel at once

    scrollToEnd();

    expect(props.onLoadMore).toHaveBeenCalledTimes(2);
    expect(symbols()).toHaveLength(7);
    expect(screen.getByText('Showing 7 of 3,204')).toBeInTheDocument();
  });

  it('does not ask while a page is loading, and shows a spinner row instead', () => {
    const props = infinite({ loadingMore: true });
    render(view({ infinite: props }));

    scrollToEnd();

    expect(props.onLoadMore).not.toHaveBeenCalled();
    expect(observedCount()).toBe(0);
    expect(screen.getByRole('progressbar', { name: 'Loading more rows' })).toBeInTheDocument();
  });

  it('does not ask for more of a stale list that is being replaced', () => {
    const props = infinite();
    render(view({ infinite: props, loading: true }));

    scrollToEnd();

    expect(props.onLoadMore).not.toHaveBeenCalled();
    expect(grid()).toHaveAttribute('aria-busy', 'true');
  });

  it('shows "Could not load more rows" with Retry, and stops loading until it is pressed', async () => {
    const props = infinite({ error: 'The instruments could not be loaded.' });
    render(view({ infinite: props }));

    scrollToEnd();
    expect(props.onLoadMore).not.toHaveBeenCalled();
    expect(symbols()).toHaveLength(5); // the loaded rows stay
    expect(screen.getByText('Could not load more rows')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(props.onRetryMore).toHaveBeenCalledTimes(1);
  });

  it('numbers the rows against the whole list: aria-rowcount is the total, rows are numbered from the top', () => {
    render(view({ infinite: infinite({ total: 500 }) }));

    expect(grid()).toHaveAttribute('aria-rowcount', '501'); // the header is row 1
    expect(dataRows().map((r) => r.getAttribute('aria-rowindex'))).toEqual(['2', '3', '4', '5', '6']);
  });

  it('names the select-all box for the loaded rows and keeps selections when more rows arrive', async () => {
    const { rerender } = render(<Stateful infinite={infinite()} />);
    await userEvent.click(screen.getByRole('checkbox', { name: 'Select all loaded rows' }));
    expect(screen.getAllByRole('checkbox', { checked: true })).toHaveLength(6);

    rerender(<Stateful infinite={infinite()} rows={[...rows, ...more]} />);

    expect(screen.getByRole('checkbox', { name: 'Select ALPHA' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Select FOXTROT' })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Select all loaded rows' })).toHaveAttribute(
      'data-indeterminate',
      'true',
    );
  });

  describe('keyboard', () => {
    it.each(['{PageDown}', '{End}', '{ArrowDown}'])('%s at the last loaded row asks for more', async (key) => {
      const props = infinite();
      render(view({ infinite: props, onRowClick: vi.fn() }));
      dataRows()[4]!.focus();

      await userEvent.keyboard(key);

      expect(props.onLoadMore).toHaveBeenCalledTimes(1);
      expect(dataRows()[4]).toHaveFocus();
    });

    it('does not ask when it is not at the end, when everything is loaded, or while loading', async () => {
      const props = infinite();
      const { rerender } = render(view({ infinite: props, onRowClick: vi.fn() }));
      dataRows()[0]!.focus();
      await userEvent.keyboard('{ArrowDown}{PageUp}{Home}');
      expect(props.onLoadMore).not.toHaveBeenCalled();

      dataRows()[4]!.focus();
      rerender(view({ infinite: { ...props, hasMore: false }, onRowClick: vi.fn() }));
      await userEvent.keyboard('{End}');
      rerender(view({ infinite: { ...props, loadingMore: true }, onRowClick: vi.fn() }));
      await userEvent.keyboard('{End}');
      expect(props.onLoadMore).not.toHaveBeenCalled();
    });
  });

  describe('a new list', () => {
    function spyOnScroll() {
      const set = vi.fn();
      Object.defineProperty(grid().parentElement!, 'scrollTop', { configurable: true, get: () => 300, set });
      return set;
    }

    it('scrolls to the top and makes the first row the tab stop when resetKey changes, not when rows are added', () => {
      const props = infinite();
      const { rerender } = render(view({ infinite: props, resetKey: 'a', onRowClick: vi.fn() }));
      dataRows()[3]!.focus();
      const set = spyOnScroll();

      rerender(view({ infinite: props, resetKey: 'a', rows: [...rows, ...more], onRowClick: vi.fn() }));
      expect(set).not.toHaveBeenCalled();
      expect(dataRows().map((r) => r.tabIndex)).toEqual([-1, -1, -1, 0, -1, -1, -1]);

      rerender(view({ infinite: props, resetKey: 'b', rows: more, onRowClick: vi.fn() }));
      expect(set).toHaveBeenCalledWith(0);
      expect(dataRows().map((r) => r.tabIndex)).toEqual([0, -1]);
    });

    it('does the same when the sort order changes', () => {
      const props = infinite();
      const { rerender } = render(view({ infinite: props, sort: { columnId: 'symbol', descending: false } }));
      const set = spyOnScroll();

      rerender(view({ infinite: props, sort: { columnId: 'symbol', descending: true } }));

      expect(set).toHaveBeenCalledWith(0);
    });
  });
});

describe('Grid: loading, empty and error', () => {
  it('shows skeleton rows on the first load, without data rows, and is busy', () => {
    render(view({ rows: [], loading: true, emptyState: <p>Nothing here</p> }));

    expect(grid()).toHaveAttribute('aria-busy', 'true');
    expect(screen.queryByText('Nothing here')).not.toBeInTheDocument(); // loading, not empty
    expect(screen.queryByText('ALPHA')).not.toBeInTheDocument();
    expect(document.querySelectorAll('.MuiSkeleton-root').length).toBeGreaterThan(0);
  });

  it('keeps the previous rows while the next ones load', () => {
    render(view({ loading: true }));

    expect(symbols()).toHaveLength(5);
    expect(grid()).toHaveAttribute('aria-busy', 'true');
    expect(document.querySelectorAll('.MuiSkeleton-root')).toHaveLength(0);
  });

  it('shows the empty state when there are no rows', () => {
    render(view({ rows: [], emptyState: <p>Nothing here</p> }));
    expect(screen.getByText('Nothing here')).toBeInTheDocument();
  });

  it('lets the keyboard reach the scrolling region only when no row can take focus', () => {
    const { unmount } = render(view({ rows: [], emptyState: <p>Nothing here</p> }));
    expect(screen.getByRole('region', { name: 'Things, scrolls sideways' })).toHaveAttribute('tabindex', '0');
    unmount();
    render(view());
    expect(screen.getByRole('region', { name: 'Things, scrolls sideways' })).not.toHaveAttribute('tabindex');
  });

  it('shows the error with a retry instead of the rows', async () => {
    const onRetry = vi.fn();
    render(view({ error: 'The list could not be loaded.', onRetry }));

    expect(screen.getByRole('alert')).toHaveTextContent('The list could not be loaded.');
    expect(screen.queryByRole('grid')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });
});

describe('Grid: scroll structure', () => {
  const scroller = () => grid().parentElement!;

  it('scrolls the body in its own container, with the footer outside it', () => {
    render(view({ infinite: { total: 5, hasMore: false, loadingMore: false, onLoadMore: vi.fn() } }));

    expect(scroller()).toHaveStyle({ overflow: 'auto', minHeight: '0px' });
    const footer = screen.getByText('Showing 5 of 5');
    expect(scroller().contains(footer)).toBe(false);
    expect(scroller().parentElement!.contains(footer)).toBe(true);
    // The grid root is a flex item that can shrink, so a parent with a height makes the body scroll.
    expect(scroller().parentElement).toHaveStyle({ display: 'flex', flexDirection: 'column', minHeight: '0px' });
  });

  it('keeps the header row sticky inside the scroll container', () => {
    render(view());
    expect(screen.getByRole('columnheader', { name: 'Symbol' })).toHaveStyle({ position: 'sticky', top: '0px' });
  });

  it('can drop the sticky header, and can pin the first column (after the checkbox column)', () => {
    const { unmount } = render(view({ stickyHeader: false }));
    expect(screen.getByRole('columnheader', { name: 'Symbol' })).not.toHaveStyle({ position: 'sticky' });
    unmount();

    render(<Stateful stickyFirstColumn />);
    expect(screen.getByRole('columnheader', { name: 'Symbol' })).toHaveStyle({ position: 'sticky', left: '48px' });
    expect(within(dataRows()[0]!).getAllByRole('gridcell')[1]).toHaveStyle({ position: 'sticky', left: '48px' });
    expect(within(dataRows()[0]!).getAllByRole('gridcell')[2]).not.toHaveStyle({ position: 'sticky' });
  });

  it('uses taller rows in the comfortable density', () => {
    render(view({ density: 'comfortable' }));
    expect(within(dataRows()[0]!).getAllByRole('gridcell')[0]).toHaveStyle({ height: '48px' });
  });
});

describe('Grid: new list resets position', () => {
  const infinite: GridInfinite = { total: 500, hasMore: true, loadingMore: false, onLoadMore: vi.fn() };
  const otherList: Row[] = [
    { id: 'f', symbol: 'FOXTROT', name: 'Foxtrot Ltd', lot: 6 },
    { id: 'g', symbol: 'GOLF', name: 'Golf Ltd', lot: 7 },
  ];

  /** Gives the scroll container a spy on `scrollTop`, as jsdom has no scrolling. */
  function spyOnScroll() {
    const scroller = grid().parentElement!;
    const set = vi.fn();
    Object.defineProperty(scroller, 'scrollTop', { configurable: true, get: () => 120, set });
    return set;
  }

  it('scrolls back to the top and makes the first row the tab stop when the resetKey changes', () => {
    const { rerender } = render(view({ infinite, resetKey: 'all', onRowClick: vi.fn() }));
    dataRows()[3]!.focus();
    expect(dataRows().map((r) => r.tabIndex)).toEqual([-1, -1, -1, 0, -1]);
    const set = spyOnScroll();

    rerender(view({ infinite, resetKey: 'banks', rows: otherList, onRowClick: vi.fn() }));

    expect(set).toHaveBeenCalledWith(0);
    expect(dataRows().map((r) => r.tabIndex)).toEqual([0, -1]);
  });

  it('does the same when the sort order changes, and not for unrelated renders or more rows arriving', () => {
    const { rerender } = render(view({ infinite, resetKey: 'all', sort: { columnId: 'symbol', descending: false } }));
    dataRows()[2]!.focus();
    const set = spyOnScroll();

    rerender(
      view({ infinite, resetKey: 'all', sort: { columnId: 'symbol', descending: false }, density: 'comfortable' }),
    );
    expect(set).not.toHaveBeenCalled();
    expect(dataRows().map((r) => r.tabIndex)).toEqual([-1, -1, 0, -1, -1]);

    rerender(
      view({
        infinite,
        resetKey: 'all',
        rows: [...rows, ...otherList],
        sort: { columnId: 'symbol', descending: false },
      }),
    );
    expect(set).not.toHaveBeenCalled();
    expect(dataRows()[2]!.tabIndex).toBe(0);

    rerender(view({ infinite, resetKey: 'all', sort: { columnId: 'symbol', descending: true } }));
    expect(set).toHaveBeenCalledTimes(1);
    expect(dataRows().map((r) => r.tabIndex)).toEqual([0, -1, -1, -1, -1]);

    dataRows()[2]!.focus();
    rerender(view({ infinite, resetKey: 'banks', sort: { columnId: 'symbol', descending: true } }));
    expect(set).toHaveBeenCalledTimes(2);
    expect(dataRows()[0]!.tabIndex).toBe(0);
  });
});

describe('Grid: keys that belong to something else', () => {
  const withHandle: GridColumn<Row>[] = [
    { id: 'handle', header: 'Reorder', kind: 'handle', render: (r) => <button type="button">Drag {r.id}</button> },
    ...columns,
  ];

  it('ignores keys from buttons and inputs inside cells, and from the handle column', async () => {
    const onRowClick = vi.fn();
    const editable: GridColumn<Row>[] = [
      ...withHandle,
      { id: 'edit', header: 'Edit', render: (r) => <input aria-label={`Edit ${r.id}`} defaultValue="x" /> },
    ];
    render(<Stateful columns={editable} onRowClick={onRowClick} />);

    screen.getByRole('button', { name: 'Drag a' }).focus();
    await userEvent.keyboard('{ArrowDown}');
    expect(screen.getByRole('button', { name: 'Drag a' })).toHaveFocus();

    screen.getByRole('textbox', { name: 'Edit b' }).focus();
    await userEvent.keyboard('{ArrowDown}{ArrowUp}{Home}{End} ');
    expect(screen.getByRole('textbox', { name: 'Edit b' })).toHaveFocus();
    expect(screen.getByRole('checkbox', { name: 'Select BRAVO' })).not.toBeChecked(); // Space typed into the input
    expect(onRowClick).not.toHaveBeenCalled();
  });

  it('does not open the row when a handle is clicked', async () => {
    const onRowClick = vi.fn();
    render(view({ columns: withHandle, onRowClick }));

    await userEvent.click(screen.getByRole('button', { name: 'Drag c' }));
    expect(onRowClick).not.toHaveBeenCalled();
    await userEvent.click(screen.getByText('Charlie Ltd'));
    expect(onRowClick).toHaveBeenCalledTimes(1);
  });

  it('ignores keys with Ctrl, Meta or Alt, and keys somebody already handled', async () => {
    const onRowClick = vi.fn();
    render(view({ onRowClick }));
    dataRows()[1]!.focus();

    await userEvent.keyboard('{Control>}{ArrowDown}{/Control}{Alt>}{ArrowDown}{/Alt}{Meta>}{Enter}{/Meta}');
    expect(dataRows()[1]).toHaveFocus();
    expect(onRowClick).not.toHaveBeenCalled();

    dataRows()[1]!.addEventListener('keydown', (event) => {
      event.preventDefault(); // React listens at the root, so this handler is first
    });
    await userEvent.keyboard('{ArrowDown}');
    expect(dataRows()[1]).toHaveFocus();
    await userEvent.keyboard('{Enter}');
    expect(onRowClick).not.toHaveBeenCalled();
  });

  it('moves between rows with the arrow keys from a row checkbox, but leaves Space to the checkbox', async () => {
    render(<Stateful />);
    const box = screen.getByRole('checkbox', { name: 'Select ALPHA' });
    box.focus();

    await userEvent.keyboard('{ArrowDown}');
    expect(dataRows()[1]).toHaveFocus();

    box.focus();
    await userEvent.keyboard(' ');
    expect(box).toBeChecked(); // the checkbox's own Space
  });
});

describe('Grid: tab order and attributes with selection', () => {
  it('has one stop in the header (select all) and one for the rows: row checkboxes are not tab stops', async () => {
    render(
      <>
        <button type="button">before</button>
        <Stateful columns={columns.map((c) => ({ ...c, sortable: false }))} />
        <button type="button">after</button>
      </>,
    );
    await userEvent.tab();
    expect(screen.getByRole('button', { name: 'before' })).toHaveFocus();
    await userEvent.tab();
    expect(screen.getByRole('checkbox', { name: 'Select all rows' })).toHaveFocus();
    await userEvent.tab();
    expect(dataRows()[0]).toHaveFocus();
    await userEvent.tab();
    expect(screen.getByRole('button', { name: 'after' })).toHaveFocus();
    expect(screen.getByRole('checkbox', { name: 'Select BRAVO' })).toHaveAttribute('tabindex', '-1');
  });

  it('numbers rows and columns for assistive technology (header is row 1, the total of an infinite list is the row count)', () => {
    render(<Stateful infinite={{ total: 500, hasMore: true, loadingMore: false, onLoadMore: vi.fn() }} />);

    expect(grid()).toHaveAttribute('aria-multiselectable', 'true');
    expect(grid()).toHaveAttribute('aria-colcount', '5'); // checkbox + four columns
    expect(grid()).toHaveAttribute('aria-rowcount', '501');
    expect(within(grid()).getAllByRole('row')[0]).toHaveAttribute('aria-rowindex', '1');
    expect(dataRows().map((r) => r.getAttribute('aria-rowindex'))).toEqual(['2', '3', '4', '5', '6']);
    expect(
      within(dataRows()[0]!)
        .getAllByRole('gridcell')
        .map((c) => c.getAttribute('aria-colindex')),
    ).toEqual(['1', '2', '3', '4', '5']);
  });

  it('is not multiselectable without selection', () => {
    render(view());
    expect(grid()).not.toHaveAttribute('aria-multiselectable');
    expect(grid()).toHaveAttribute('aria-colcount', '4');
  });
});

describe('Grid: reuse hooks and rendering cost', () => {
  it('adds the extras of getRowProps to a row: attributes, styles and a ref', () => {
    const ref = vi.fn();
    const extras = { attributes: { 'data-kind': 'member' }, sx: { fontStyle: 'italic' }, ref } as const;
    render(view({ getRowProps: (row) => (row.id === 'b' ? extras : undefined) }));

    expect(dataRows()[1]).toHaveAttribute('data-kind', 'member');
    expect(dataRows()[1]).toHaveStyle({ fontStyle: 'italic' });
    expect(dataRows()[1]).toHaveAttribute('role', 'row'); // the grid's own semantics stay
    expect(dataRows()[0]).not.toHaveAttribute('data-kind');
    expect(ref).toHaveBeenCalledWith(dataRows()[1]);
  });

  it('re-renders only the row whose selection changed, and not at all for keyboard movement', async () => {
    const renders: Record<string, number> = {};
    const counted: GridColumn<Row>[] = [
      {
        id: 'symbol',
        header: 'Symbol',
        render: (r) => {
          renders[r.id] = (renders[r.id] ?? 0) + 1;
          return r.symbol;
        },
      },
    ];
    render(<Stateful columns={counted} onRowClick={vi.fn()} />);
    const before = { ...renders };

    await userEvent.click(screen.getByRole('checkbox', { name: 'Select BRAVO' }));
    expect(renders.b).toBe((before.b ?? 0) + 1);
    for (const id of ['a', 'c', 'd', 'e']) expect(renders[id]).toBe(before[id]);

    const afterSelect = { ...renders };
    dataRows()[0]!.focus();
    await userEvent.keyboard('{ArrowDown}{ArrowDown}{ArrowUp}');
    expect(renders).toEqual(afterSelect);
  });
});
