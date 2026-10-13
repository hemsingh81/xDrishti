import { act, render } from '@testing-library/react';
import type { ReactElement } from 'react';
import type { CategoryChartCanvasProps } from './CategoryChartCanvas';

// The real module, not the stand-in every other test gets (setup.ts); the library underneath is replaced by a fake that
// records what the canvas asks of it, since jsdom cannot draw.
const lib = vi.hoisted(() => {
  const chart = { setOption: vi.fn(), resize: vi.fn(), dispose: vi.fn(), on: vi.fn(), getWidth: vi.fn(() => 600) };
  return { chart, init: vi.fn(() => chart), use: vi.fn() };
});
vi.mock('echarts/core', () => ({ init: lib.init, use: lib.use }));
vi.mock('echarts/charts', () => ({ PieChart: 'PieChart', BarChart: 'BarChart' }));
vi.mock('echarts/components', () => ({ GridComponent: 'GridComponent' }));
vi.mock('echarts/renderers', () => ({ CanvasRenderer: 'CanvasRenderer' }));

const actual = await vi.importActual<{ default: (props: CategoryChartCanvasProps) => ReactElement }>(
  './CategoryChartCanvas',
);
const CategoryChartCanvas = actual.default;
const registered = lib.use.mock.calls.slice();

class FakeResizeObserver {
  static instances: FakeResizeObserver[] = [];
  disconnected = false;
  constructor(readonly callback: () => void) {
    FakeResizeObserver.instances.push(this);
  }
  observe = vi.fn();
  disconnect() {
    this.disconnected = true;
  }
}

const props = (overrides: Partial<CategoryChartCanvasProps> = {}): CategoryChartCanvasProps => ({
  option: { series: [] },
  onHover: () => undefined,
  ...overrides,
});
const handler = (event: string) =>
  (lib.chart.on.mock.calls as unknown as [string, (e: unknown) => void][]).find(([name]) => name === event)?.[1];

beforeEach(() => {
  vi.clearAllMocks();
  FakeResizeObserver.instances = [];
  vi.stubGlobal('ResizeObserver', FakeResizeObserver);
});
afterEach(() => {
  vi.unstubAllGlobals();
});

describe('CategoryChartCanvas', () => {
  it('registers only the pie, the bar, the grid and the canvas renderer', () => {
    expect(registered).toEqual([[['PieChart', 'BarChart', 'GridComponent', 'CanvasRenderer']]]);
  });

  it('draws on a canvas in its own element and applies each new option from scratch', () => {
    const option = { series: [{ type: 'pie' }] };
    const { rerender } = render(<CategoryChartCanvas {...props({ option })} />);
    expect(lib.init).toHaveBeenCalledWith(expect.any(HTMLElement), undefined, { renderer: 'canvas' });
    expect(lib.chart.setOption).toHaveBeenCalledWith(option, { notMerge: true });

    const next = { series: [{ type: 'bar' }] };
    rerender(<CategoryChartCanvas {...props({ option: next })} />);
    expect(lib.init).toHaveBeenCalledTimes(1);
    expect(lib.chart.setOption).toHaveBeenLastCalledWith(next, { notMerge: true });
  });

  it('follows the size of its container and lets go of everything when removed', () => {
    const { unmount } = render(<CategoryChartCanvas {...props()} />);
    const observer = FakeResizeObserver.instances[0];
    act(() => {
      observer?.callback();
    });
    expect(lib.chart.resize).toHaveBeenCalledTimes(1);

    unmount();
    expect(observer?.disconnected).toBe(true);
    expect(lib.chart.dispose).toHaveBeenCalledTimes(1);
  });

  it('works without ResizeObserver', () => {
    vi.stubGlobal('ResizeObserver', undefined);
    expect(() => render(<CategoryChartCanvas {...props()} />)).not.toThrow();
  });

  it('tells which slice or bar the pointer is on, where, and how wide the chart is', () => {
    const onHover = vi.fn();
    render(<CategoryChartCanvas {...props({ onHover })} />);

    handler('mouseover')?.({ dataIndex: 2, event: { offsetX: 120, offsetY: 40 } });

    expect(onHover).toHaveBeenLastCalledWith({ index: 2, x: 120, y: 40, width: 600 });
  });

  it('tells when the pointer leaves the element or the chart, and ignores events without a position', () => {
    const onHover = vi.fn();
    render(<CategoryChartCanvas {...props({ onHover })} />);

    handler('mouseover')?.({ dataIndex: 1 });
    expect(onHover).toHaveBeenLastCalledWith(null);

    onHover.mockClear();
    handler('mouseout')?.({});
    handler('globalout')?.({});
    expect(onHover).toHaveBeenCalledTimes(2);
    expect(onHover).toHaveBeenLastCalledWith(null);
  });

  it('is hidden from assistive technology: the wrapper around it carries the description', () => {
    const { container } = render(<CategoryChartCanvas {...props()} />);
    expect(container.firstElementChild).toHaveAttribute('aria-hidden', 'true');
  });
});
