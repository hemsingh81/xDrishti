import { ThemeProvider } from '@mui/material/styles';
import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { categoryStub, drawnCategorySeries } from '@/test/chartStub';
import { scrollToEnd } from '@/test/intersection';
import { stubPhoneWidth, stubReducedMotion } from '@/test/matchMedia';
import { categorical } from '@/theme/tokens';
import { createAppTheme } from '@/theme/theme';
import type { ColorMode } from '@/theme/tokens';
import { CategoryChart, type CategoryChartProps } from './CategoryChart';
import type { CategoryData } from './categoryTypes';

const money = (v: number) => `₹${String(v)}`;

const donut: CategoryData = {
  kind: 'donut',
  slices: [
    { id: 'a', name: 'Banking', value: 600, color: 0 },
    { id: 'b', name: 'IT', value: 300, color: 1 },
    { id: 'c', name: 'Other', value: 100, color: 8 },
  ],
};

const stacked: CategoryData = {
  kind: 'stacked',
  categories: [
    { id: 'banking', name: 'Banking' },
    { id: 'it', name: 'IT' },
  ],
  stacks: [
    { id: 'main', name: 'Main', color: 0, values: [400, 300] },
    { id: 'swing', name: 'Swing', color: 1, values: [200, 0] },
  ],
};

function renderChart(props: Partial<CategoryChartProps> = {}, mode: ColorMode = 'light') {
  return render(
    <ThemeProvider theme={createAppTheme(mode)}>
      <CategoryChart label="Allocation by sector" data={donut} valueFormatter={money} height={200} {...props} />
    </ThemeProvider>,
  );
}

describe('CategoryChart: a donut', () => {
  it('shows a skeleton of the chart height while loading, and no chart or controls', () => {
    renderChart({ loading: true });
    expect(screen.getByRole('status', { name: 'Loading chart' })).toBeInTheDocument();
    expect(screen.queryByTestId('category-canvas')).not.toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('shows the empty content when there is nothing to draw', () => {
    renderChart({ data: { kind: 'donut', slices: [] }, empty: <p>Nothing to show</p> });
    expect(screen.getByText('Nothing to show')).toBeInTheDocument();
    expect(screen.queryByTestId('category-canvas')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Show as table' })).not.toBeInTheDocument();
  });

  it('draws under an image role whose name is the summary: every slice with its share and value', async () => {
    renderChart();
    const chart = await screen.findByRole('img', { name: /Allocation by sector, 3 slices, total ₹1000/ });
    expect(chart).toHaveAccessibleName(
      expect.stringContaining('Banking 60.0% (₹600); IT 30.0% (₹300); Other 10.0% (₹100)'),
    );
    expect(within(chart).getByTestId('category-canvas')).toBeInTheDocument();
  });

  it('labels the slices with name and share, in the palette colours of the theme', async () => {
    renderChart();
    await screen.findByTestId('category-canvas');
    const [pie] = drawnCategorySeries();
    expect(pie?.type).toBe('pie');
    expect(pie?.data).toMatchObject([
      { name: 'Banking', value: 600, label: { formatter: 'Banking\n60.0%' } },
      { name: 'IT', value: 300, label: { formatter: 'IT\n30.0%' } },
      { name: 'Other', value: 100, label: { formatter: 'Other\n10.0%' } },
    ]);
  });

  it.each(['light', 'dark'] as const)('takes the slice colours from the %s categorical palette', async (mode) => {
    renderChart({}, mode);
    await screen.findByTestId('category-canvas');
    const colours = (drawnCategorySeries()[0]?.data ?? []).map((d) => (typeof d === 'number' ? d : d.itemStyle.color));
    expect(colours).toEqual([categorical[mode][0], categorical[mode][1], categorical[mode][8]]);
  });

  it('turns the animation off for a visitor who prefers reduced motion, and on otherwise', async () => {
    const { unmount } = renderChart();
    await screen.findByTestId('category-canvas');
    expect(categoryStub.last?.option).toMatchObject({ animation: true });
    unmount();

    stubReducedMotion();
    renderChart();
    await screen.findByTestId('category-canvas');
    expect(categoryStub.last?.option).toMatchObject({ animation: false });
  });

  it('leaves the names off the slices on a phone (the legend has them)', async () => {
    stubPhoneWidth();
    renderChart();
    await screen.findByTestId('category-canvas');
    expect(drawnCategorySeries()[0]).toMatchObject({ label: { show: false } });
  });

  it('offers a toggle button per slice with its share, outside the canvas, and redraws without a hidden slice', async () => {
    renderChart();
    await screen.findByTestId('category-canvas');
    const legend = screen.getByRole('group', { name: 'Slices shown' });
    const banking = within(legend).getByRole('button', { name: 'Banking 60.0%' });
    expect(banking).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(banking);

    expect(banking).toHaveAttribute('aria-pressed', 'false');
    expect(banking).toHaveAccessibleName('Banking');
    expect(drawnCategorySeries()[0]?.data).toMatchObject([
      { name: 'IT', label: { formatter: 'IT\n75.0%' } },
      { name: 'Other', label: { formatter: 'Other\n25.0%' } },
    ]);
    expect(screen.getByRole('img')).toHaveAccessibleName(expect.not.stringContaining('Banking'));

    await userEvent.click(banking);
    expect(drawnCategorySeries()[0]?.data).toHaveLength(3);
  });

  it('keeps the last visible slice: it cannot be hidden, and says why', async () => {
    renderChart();
    await screen.findByTestId('category-canvas');
    await userEvent.click(screen.getByRole('button', { name: /^Banking/ }));
    await userEvent.click(screen.getByRole('button', { name: /^IT/ }));

    const last = screen.getByRole('button', { name: /^Other/ });
    expect(last).toBeDisabled();
    expect(last).toHaveAccessibleDescription('At least one must stay visible');
    expect(drawnCategorySeries()[0]?.data).toHaveLength(1);
  });

  it('shows everything again when new data leaves no slice switched on', async () => {
    const { rerender } = renderChart();
    await screen.findByTestId('category-canvas');
    await userEvent.click(screen.getByRole('button', { name: /^Banking/ }));
    await userEvent.click(screen.getByRole('button', { name: /^IT/ }));

    rerender(
      <ThemeProvider theme={createAppTheme('light')}>
        <CategoryChart
          label="Allocation by sector"
          data={{
            kind: 'donut',
            slices: [
              { id: 'y', name: 'Auto', value: 4, color: 0 },
              { id: 'z', name: 'Energy', value: 5, color: 1 },
            ],
          }}
          valueFormatter={money}
        />
      </ThemeProvider>,
    );

    expect(await screen.findByRole('button', { name: /^Energy/ })).toBeEnabled();
    expect(screen.getByRole('button', { name: /^Auto/ })).toBeEnabled();
    expect(drawnCategorySeries()[0]?.data).toHaveLength(2);
  });

  it('reaches the legend and the table toggle with the keyboard', async () => {
    renderChart();
    await screen.findByTestId('category-canvas');
    await userEvent.tab();
    expect(screen.getByRole('button', { name: 'Banking 60.0%' })).toHaveFocus();
    await userEvent.keyboard('{Tab}{Tab}{Tab}');
    expect(screen.getByRole('button', { name: 'Show as table' })).toHaveFocus();
    await userEvent.keyboard('{Enter}');
    expect(screen.getByRole('table', { name: 'Allocation by sector, data table' })).toBeInTheDocument();
  });

  it('shows the figures as a table: name, value and share', async () => {
    renderChart();
    await screen.findByTestId('category-canvas');
    const toggle = screen.getByRole('button', { name: 'Show as table' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(toggle);

    expect(screen.getByRole('button', { name: 'Hide table' })).toHaveAttribute('aria-expanded', 'true');
    const table = screen.getByRole('table', { name: 'Allocation by sector, data table' });
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((h) => h.textContent),
    ).toEqual(['Name', 'Value', 'Share']);
    const rows = within(table).getAllByRole('row').slice(1);
    expect(
      rows.map((r) => [
        within(r).getByRole('rowheader').textContent,
        ...within(r)
          .getAllByRole('cell')
          .map((c) => c.textContent),
      ]),
    ).toEqual([
      ['Banking', '₹600', '60.0%'],
      ['IT', '₹300', '30.0%'],
      ['Other', '₹100', '10.0%'],
    ]);

    await userEvent.click(screen.getByRole('button', { name: 'Hide table' }));
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('loads the chart only when it is about to be seen, with a skeleton until then', async () => {
    renderChart({ deferUntilVisible: true });
    expect(screen.getByRole('status', { name: 'Loading chart' })).toBeInTheDocument();
    expect(screen.queryByTestId('category-canvas')).not.toBeInTheDocument();

    scrollToEnd();

    expect(await screen.findByTestId('category-canvas')).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Loading chart' })).not.toBeInTheDocument();
  });

  it('dims the chart while newer data loads', async () => {
    renderChart({ busy: true });
    await screen.findByTestId('category-canvas');
    expect(screen.getByRole('img').parentElement).toHaveAttribute('aria-busy', 'true');
  });

  it('shows the slice, its share and value where the pointer is, hidden from assistive technology', async () => {
    renderChart();
    await screen.findByTestId('category-canvas');

    act(() => {
      categoryStub.last?.onHover({ index: 1, x: 100, y: 50, width: 600 });
    });

    const tooltip = screen.getByText('₹300').closest('.MuiPaper-root');
    expect(tooltip).toHaveTextContent('IT');
    expect(tooltip).toHaveTextContent('30.0%');
    expect(tooltip).toHaveAttribute('aria-hidden', 'true');

    act(() => {
      categoryStub.last?.onHover(null);
    });
    expect(screen.queryByText('₹300')).not.toBeInTheDocument();
  });

  it('says so and shows the table when the chart cannot be loaded', async () => {
    categoryStub.fail = true;
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    renderChart();

    expect(await screen.findByText(/The chart could not be loaded/)).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Allocation by sector, data table' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Hide table' })).toBeDisabled();
  });

  it('tries to load the chart again on request, and drops the table fallback when it works', async () => {
    categoryStub.fail = true;
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    renderChart();
    await screen.findByText(/The chart could not be loaded/);

    categoryStub.fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByTestId('category-canvas')).toBeInTheDocument();
    expect(screen.queryByText(/The chart could not be loaded/)).not.toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });
});

describe('CategoryChart: a stacked bar', () => {
  const renderStacked = () => renderChart({ label: 'Value by sector, split by account', data: stacked });

  it('draws one bar series per account, stacked, with the sectors as rows', async () => {
    renderStacked();
    await screen.findByTestId('category-canvas');
    expect(drawnCategorySeries().map((s) => [s.name, s.type, s.data, s.itemStyle?.color])).toEqual([
      ['Main', 'bar', [400, 300], categorical.light[0]],
      ['Swing', 'bar', [200, 0], categorical.light[1]],
    ]);
    expect(categoryStub.last?.option).toMatchObject({ yAxis: { type: 'category', data: ['Banking', 'IT'] } });
  });

  it('describes every bar in the summary with its split', async () => {
    renderStacked();
    const chart = await screen.findByRole('img', { name: /Value by sector, split by account, 2 bars/ });
    expect(chart).toHaveAccessibleName(
      expect.stringContaining('Banking ₹600 (Main ₹400, Swing ₹200); IT ₹300 (Main ₹300)'),
    );
  });

  it('has a legend of the accounts; hiding one redraws the bars without it', async () => {
    renderStacked();
    await screen.findByTestId('category-canvas');
    const legend = screen.getByRole('group', { name: 'Series shown' });
    await userEvent.click(within(legend).getByRole('button', { name: 'Swing' }));

    expect(drawnCategorySeries().map((s) => s.name)).toEqual(['Main']);
    expect(screen.getByRole('img')).toHaveAccessibleName(expect.stringContaining('Banking ₹400 (Main ₹400)'));
  });

  it('shows the matrix as a table with totals and a dash where an account holds none', async () => {
    renderStacked();
    await screen.findByTestId('category-canvas');
    await userEvent.click(screen.getByRole('button', { name: 'Show as table' }));

    const table = screen.getByRole('table', { name: 'Value by sector, split by account, data table' });
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((h) => h.textContent),
    ).toEqual(['', 'Main', 'Swing', 'Total']);
    const rows = within(table).getAllByRole('row').slice(1);
    expect(
      rows.map((r) => [
        within(r).getByRole('rowheader').textContent,
        ...within(r)
          .getAllByRole('cell')
          .map((c) => c.textContent),
      ]),
    ).toEqual([
      ['Banking', '₹400', '₹200', '₹600'],
      ['IT', '₹300', '—', '₹300'],
    ]);
  });

  it('shows the bar total and its split in the tooltip, leaving out accounts with none', async () => {
    renderStacked();
    await screen.findByTestId('category-canvas');

    act(() => {
      categoryStub.last?.onHover({ index: 1, x: 500, y: 40, width: 600 });
    });

    const tooltip = screen.getByText('IT · ₹300').closest('.MuiPaper-root');
    expect(tooltip).toHaveTextContent('Main₹300');
    expect(tooltip).not.toHaveTextContent('Swing');
    expect(tooltip).toHaveAttribute('aria-hidden', 'true');
  });
});
