import { ThemeProvider } from '@mui/material/styles';
import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { scrollToEnd } from '@/test/intersection';
import { stubReducedMotion } from '@/test/matchMedia';
import { chartStub, drawnSeries } from '@/test/chartStub';
import { createAppTheme } from '@/theme/theme';
import { palettes, type ColorMode } from '@/theme/tokens';
import { TimeSeriesChart, type TimeSeriesChartProps } from './TimeSeriesChart';
import type { ChartSeries } from './types';

const money = (v: number) => `₹${String(v)}`;
const day = (d: string) => `<${d}>`;

const days = ['2026-09-01', '2026-09-02', '2026-09-03'];
const manyDays = Array.from({ length: 45 }, (_, i) => new Date(Date.UTC(2026, 7, 1 + i)).toISOString().slice(0, 10));

const value: ChartSeries = {
  id: 'value',
  name: 'Value',
  color: 'accent',
  data: days.slice(0, 3).map((d, i) => [d, 100 + i * 10]),
};
const invested: ChartSeries = {
  id: 'invested',
  name: 'Invested',
  color: 'neutral',
  dash: 'dashed',
  data: days.slice(0, 3).map((d) => [d, 100]),
};

function renderChart(props: Partial<TimeSeriesChartProps> = {}, mode: ColorMode = 'light') {
  const ui = (p: Partial<TimeSeriesChartProps>) => (
    <ThemeProvider theme={createAppTheme(mode)}>
      <TimeSeriesChart
        label="Portfolio value history"
        series={[value, invested]}
        valueFormatter={money}
        dateFormatter={day}
        height={200}
        {...p}
      />
    </ThemeProvider>
  );
  const result = render(ui(props));
  return result;
}

describe('TimeSeriesChart', () => {
  it('shows a skeleton of the chart height while loading, and no chart', () => {
    renderChart({ loading: true });
    expect(screen.getByRole('status', { name: 'Loading chart' })).toBeInTheDocument();
    expect(screen.queryByTestId('chart-canvas')).not.toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('shows the empty content when no series has a point', () => {
    renderChart({ series: [{ ...value, data: [] }], empty: <p>No history yet</p> });
    expect(screen.getByText('No history yet')).toBeInTheDocument();
    expect(screen.queryByTestId('chart-canvas')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Show as table' })).not.toBeInTheDocument();
  });

  it('draws the chart under an image role whose name is the summary: first and last value, range', async () => {
    renderChart();
    const chart = await screen.findByRole('img', { name: /Portfolio value history, 3 dates/ });
    expect(chart).toHaveAccessibleName(expect.stringContaining('Value: ₹100 on <2026-09-01>, ₹120 on <2026-09-03>'));
    expect(chart).toHaveAccessibleName(expect.stringContaining('from <2026-09-01> to <2026-09-03>'));
    expect(within(chart).getByTestId('chart-canvas')).toBeInTheDocument();
  });

  it('draws the lines with distinct styles', async () => {
    renderChart();
    await screen.findByTestId('chart-canvas');
    expect(drawnSeries().map((s) => [s.name, s.lineStyle.type])).toEqual([
      ['Value', 'solid'],
      ['Invested', 'dashed'],
    ]);
  });

  it.each(['light', 'dark'] as const)('colours the lines from the %s theme tokens', async (mode) => {
    renderChart({}, mode);
    await screen.findByTestId('chart-canvas');
    expect(drawnSeries().map((s) => s.lineStyle.color)).toEqual([palettes[mode].accent, palettes[mode].textSecondary]);
  });

  it('draws again in the new colours when the colour mode changes', async () => {
    const { rerender } = render(
      <ThemeProvider theme={createAppTheme('light')}>
        <TimeSeriesChart label="Chart" series={[value]} valueFormatter={money} />
      </ThemeProvider>,
    );
    await screen.findByTestId('chart-canvas');
    expect(drawnSeries()[0]?.lineStyle.color).toBe(palettes.light.accent);

    rerender(
      <ThemeProvider theme={createAppTheme('dark')}>
        <TimeSeriesChart label="Chart" series={[value]} valueFormatter={money} />
      </ThemeProvider>,
    );
    await vi.waitFor(() => {
      expect(drawnSeries()[0]?.lineStyle.color).toBe(palettes.dark.accent);
    });
  });

  it('turns the animation off for a visitor who prefers reduced motion, and on otherwise', async () => {
    const { unmount } = renderChart();
    await screen.findByTestId('chart-canvas');
    expect(chartStub.last?.option).toMatchObject({ animation: true });
    unmount();

    stubReducedMotion();
    renderChart();
    await screen.findByTestId('chart-canvas');
    expect(chartStub.last?.option).toMatchObject({ animation: false });
  });

  it('offers a toggle button per line, outside the canvas, and hides and shows the line', async () => {
    renderChart();
    await screen.findByTestId('chart-canvas');
    const legend = screen.getByRole('group', { name: 'Lines shown' });
    const invest = within(legend).getByRole('button', { name: 'Invested' });
    expect(invest).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(invest);
    expect(invest).toHaveAttribute('aria-pressed', 'false');
    expect(drawnSeries().map((s) => s.name)).toEqual(['Value']);
    expect(screen.getByRole('img')).toHaveAccessibleName(expect.not.stringContaining('Invested'));

    await userEvent.click(invest);
    expect(drawnSeries().map((s) => s.name)).toEqual(['Value', 'Invested']);
  });

  it('keeps the last visible line: it cannot be hidden', async () => {
    renderChart();
    await screen.findByTestId('chart-canvas');
    await userEvent.click(screen.getByRole('button', { name: 'Invested' }));
    expect(screen.getByRole('button', { name: 'Value' })).toBeDisabled();
    expect(drawnSeries().map((s) => s.name)).toEqual(['Value']);
  });

  it('says why the last visible line cannot be hidden', async () => {
    renderChart();
    await screen.findByTestId('chart-canvas');
    await userEvent.click(screen.getByRole('button', { name: 'Invested' }));

    const last = screen.getByRole('button', { name: 'Value' });
    expect(last).toBeDisabled();
    expect(last).toHaveAccessibleDescription('At least one line must stay visible');
    expect(last.parentElement).toHaveAttribute('title', 'At least one line must stay visible');
    expect(screen.getByRole('button', { name: 'Invested' })).not.toHaveAccessibleDescription();
  });

  it('keeps the zoom when a line is hidden or shown, and resets it only for other data', async () => {
    const longer: ChartSeries = { ...invested, data: [...invested.data, ['2026-09-04', 100]] };
    const { rerender } = render(
      <ThemeProvider theme={createAppTheme('light')}>
        <TimeSeriesChart label="Chart" series={[value, longer]} valueFormatter={money} />
      </ThemeProvider>,
    );
    await screen.findByTestId('chart-canvas');
    act(() => {
      chartStub.last?.onZoomChange(true);
    });
    const before = chartStub.last;

    // Hiding the line with the extra date changes the visible dates, but not the extent of the data.
    await userEvent.click(screen.getByRole('button', { name: 'Invested' }));

    expect(chartStub.last?.resetKey).toBe(before?.resetKey);
    expect(chartStub.last?.resetToken).toBe(before?.resetToken);
    expect(screen.getByRole('button', { name: 'Reset zoom' })).toBeInTheDocument();

    rerender(
      <ThemeProvider theme={createAppTheme('light')}>
        <TimeSeriesChart
          label="Chart"
          series={[{ ...value, data: [...value.data, ['2026-09-05', 1]] }, longer]}
          valueFormatter={money}
        />
      </ThemeProvider>,
    );
    await vi.waitFor(() => {
      expect(chartStub.last?.resetKey).not.toBe(before?.resetKey);
    });
  });

  it('reaches the legend and the table toggle with the keyboard', async () => {
    renderChart();
    await screen.findByTestId('chart-canvas');
    await userEvent.tab();
    expect(screen.getByRole('button', { name: 'Value' })).toHaveFocus();
    await userEvent.keyboard('{Tab}{Tab}');
    expect(screen.getByRole('button', { name: 'Show as table' })).toHaveFocus();
    await userEvent.keyboard('{Enter}');
    expect(screen.getByRole('table', { name: 'Portfolio value history, data table' })).toBeInTheDocument();
  });

  it('shows the figures as a table, newest first, with a dash where a line has no value', async () => {
    renderChart({ series: [value, { ...invested, data: invested.data.slice(1) }] });
    await screen.findByTestId('chart-canvas');
    const toggle = screen.getByRole('button', { name: 'Show as table' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(toggle);

    expect(screen.getByRole('button', { name: 'Hide table' })).toHaveAttribute('aria-expanded', 'true');
    const table = screen.getByRole('table', { name: 'Portfolio value history, data table' });
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((h) => h.textContent),
    ).toEqual(['Date', 'Value', 'Invested']);
    const rows = within(table).getAllByRole('row').slice(1);
    expect(
      rows.map((r) => [
        within(r).getByRole('rowheader').textContent,
        ...within(r)
          .getAllByRole('cell')
          .map((c) => c.textContent),
      ]),
    ).toEqual([
      ['<2026-09-03>', '₹120', '₹100'],
      ['<2026-09-02>', '₹110', '₹100'],
      ['<2026-09-01>', '₹100', '—'],
    ]);

    await userEvent.click(screen.getByRole('button', { name: 'Hide table' }));
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('pages a long table, newest first', async () => {
    const long: ChartSeries = { ...value, data: manyDays.map((d, i) => [d, i]) };
    renderChart({ series: [long] });
    await screen.findByTestId('chart-canvas');
    await userEvent.click(screen.getByRole('button', { name: 'Show as table' }));

    const table = screen.getByRole('table');
    expect(within(table).getAllByRole('row')).toHaveLength(21);
    expect(screen.getByText('Dates 1–20 of 45, newest first')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Newer' })).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: 'Older' }));
    expect(screen.getByText('Dates 21–40 of 45, newest first')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Older' }));
    expect(screen.getByText('Dates 41–45 of 45, newest first')).toBeInTheDocument();
    expect(within(table).getAllByRole('row')).toHaveLength(6);
    expect(screen.getByRole('button', { name: 'Older' })).toBeDisabled();
  });

  it('loads the chart only when it is about to be seen, with a skeleton until then', async () => {
    renderChart({ deferUntilVisible: true });
    expect(screen.getByRole('status', { name: 'Loading chart' })).toBeInTheDocument();
    expect(screen.queryByTestId('chart-canvas')).not.toBeInTheDocument();

    scrollToEnd();

    expect(await screen.findByTestId('chart-canvas')).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Loading chart' })).not.toBeInTheDocument();
  });

  it('dims the chart while newer data loads', async () => {
    renderChart({ busy: true });
    await screen.findByTestId('chart-canvas');
    expect(screen.getByRole('img').parentElement).toHaveAttribute('aria-busy', 'true');
  });

  it('shows the date and the value of every visible line where the pointer is, and hides it when it leaves', async () => {
    renderChart();
    await screen.findByTestId('chart-canvas');
    expect(screen.queryByText('<2026-09-02>')).not.toBeInTheDocument();

    act(() => {
      chartStub.last?.onHover({ index: 1, x: 100, width: 600 });
    });

    const tooltip = screen.getByText('<2026-09-02>').closest('.MuiPaper-root');
    expect(tooltip).toHaveTextContent('Value₹110');
    expect(tooltip).toHaveTextContent('Invested₹100');
    expect(tooltip).toHaveAttribute('aria-hidden', 'true');

    act(() => {
      chartStub.last?.onHover(null);
    });
    expect(screen.queryByText('<2026-09-02>')).not.toBeInTheDocument();
  });

  it('leaves a hidden line out of the tooltip, and writes a dash where a line has no value', async () => {
    renderChart({ series: [value, { ...invested, data: invested.data.slice(1) }] });
    await screen.findByTestId('chart-canvas');
    act(() => {
      chartStub.last?.onHover({ index: 0, x: 10, width: 600 });
    });
    expect(screen.getByText('<2026-09-01>').closest('.MuiPaper-root')).toHaveTextContent('Invested—');

    await userEvent.click(screen.getByRole('button', { name: 'Invested' }));
    expect(screen.getByText('<2026-09-01>').closest('.MuiPaper-root')).not.toHaveTextContent('Invested');
  });

  it('offers to reset the zoom once the canvas reports one', async () => {
    renderChart();
    await screen.findByTestId('chart-canvas');
    expect(screen.queryByRole('button', { name: 'Reset zoom' })).not.toBeInTheDocument();

    act(() => {
      chartStub.last?.onZoomChange(true);
    });
    const reset = await screen.findByRole('button', { name: 'Reset zoom' });
    const before = chartStub.last?.resetToken ?? 0;
    await userEvent.click(reset);

    expect(chartStub.last?.resetToken).toBe(before + 1);
  });

  it('says so and shows the table when the chart cannot be loaded', async () => {
    chartStub.fail = true;
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    renderChart();

    expect(await screen.findByText(/The chart could not be loaded/)).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Portfolio value history, data table' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Hide table' })).toBeDisabled();
  });

  it('tries to load the chart again on request, and drops the table fallback when it works', async () => {
    chartStub.fail = true;
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    renderChart();
    await screen.findByText(/The chart could not be loaded/);

    chartStub.fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByTestId('chart-canvas')).toBeInTheDocument();
    expect(screen.queryByText(/The chart could not be loaded/)).not.toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Show as table' })).toBeEnabled();
  });

  it('offers to try again again when the second attempt fails too', async () => {
    chartStub.fail = true;
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    renderChart();
    await userEvent.click(await screen.findByRole('button', { name: 'Try again' }));

    expect(await screen.findByText(/The chart could not be loaded/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    expect(screen.getByRole('table')).toBeInTheDocument();
  });
});
