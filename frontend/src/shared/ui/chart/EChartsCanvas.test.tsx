import { act, render } from '@testing-library/react';
import type { ReactElement } from 'react';
import type { EChartsCanvasProps } from './EChartsCanvas';

// The real module, not the stand-in every other test gets (setup.ts); the library underneath is replaced by a fake that
// records what the canvas asks of it, since jsdom cannot draw.
const lib = vi.hoisted(() => {
  const chart = {
    setOption: vi.fn(),
    resize: vi.fn(),
    dispose: vi.fn(),
    on: vi.fn(),
    dispatchAction: vi.fn(),
    getOption: vi.fn(() => ({ dataZoom: [{ start: 0, end: 100 }] })),
    convertToPixel: vi.fn(() => 120),
    getWidth: vi.fn(() => 600),
  };
  return { chart, init: vi.fn(() => chart), use: vi.fn() };
});
vi.mock('echarts/core', () => ({ init: lib.init, use: lib.use }));
vi.mock('echarts/charts', () => ({ LineChart: 'LineChart' }));
vi.mock('echarts/components', () => ({
  AxisPointerComponent: 'AxisPointerComponent',
  DataZoomInsideComponent: 'DataZoomInsideComponent',
  GridComponent: 'GridComponent',
}));
vi.mock('echarts/renderers', () => ({ CanvasRenderer: 'CanvasRenderer' }));

const actual = await vi.importActual<{ default: (props: EChartsCanvasProps) => ReactElement }>('./EChartsCanvas');
const EChartsCanvas = actual.default;
// What the module registered when it loaded (before `clearAllMocks` runs).
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

const props = (overrides: Partial<EChartsCanvasProps> = {}): EChartsCanvasProps => ({
  option: { series: [] },
  resetKey: 'a',
  resetToken: 0,
  onZoomChange: () => undefined,
  onHover: () => undefined,
  ...overrides,
});

beforeEach(() => {
  vi.clearAllMocks();
  FakeResizeObserver.instances = [];
  vi.stubGlobal('ResizeObserver', FakeResizeObserver);
});
afterEach(() => {
  vi.unstubAllGlobals();
});

describe('EChartsCanvas', () => {
  it('registers only the line chart, grid, axis pointer, inside zoom and the canvas renderer', () => {
    expect(registered).toEqual([
      [['LineChart', 'GridComponent', 'AxisPointerComponent', 'DataZoomInsideComponent', 'CanvasRenderer']],
    ]);
  });

  it('draws on a canvas in its own element and applies the option, dropping lines that left it', () => {
    const option = { series: [{ id: 'value' }] };
    render(<EChartsCanvas {...props({ option })} />);

    expect(lib.init).toHaveBeenCalledWith(expect.any(HTMLElement), undefined, { renderer: 'canvas' });
    expect(lib.chart.setOption).toHaveBeenCalledWith(option, { replaceMerge: ['series'] });
  });

  it('applies a changed option without making a new chart', () => {
    const { rerender } = render(<EChartsCanvas {...props()} />);
    const next = { series: [{ id: 'invested' }] };

    rerender(<EChartsCanvas {...props({ option: next })} />);

    expect(lib.init).toHaveBeenCalledTimes(1);
    expect(lib.chart.setOption).toHaveBeenLastCalledWith(next, { replaceMerge: ['series'] });
  });

  it('follows the size of its container and lets go of everything when removed', () => {
    const { unmount } = render(<EChartsCanvas {...props()} />);
    const observer = FakeResizeObserver.instances[0];
    expect(observer).toBeDefined();

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
    expect(() => render(<EChartsCanvas {...props()} />)).not.toThrow();
  });

  it('tells when the chart is zoomed in and when it is back at the full range', () => {
    const onZoomChange = vi.fn();
    render(<EChartsCanvas {...props({ onZoomChange })} />);
    const call = lib.chart.on.mock.calls.find(([event]) => event === 'datazoom') as unknown as [string, () => void];
    onZoomChange.mockClear();

    lib.chart.getOption.mockReturnValueOnce({ dataZoom: [{ start: 25, end: 100 }] });
    call[1]();
    expect(onZoomChange).toHaveBeenLastCalledWith(true);

    lib.chart.getOption.mockReturnValueOnce({ dataZoom: [{ start: 0, end: 100 }] });
    call[1]();
    expect(onZoomChange).toHaveBeenLastCalledWith(false);
  });

  it('resets the zoom when other data arrives or the reset is asked for', () => {
    const onZoomChange = vi.fn();
    const { rerender } = render(<EChartsCanvas {...props({ onZoomChange })} />);
    lib.chart.dispatchAction.mockClear();

    rerender(<EChartsCanvas {...props({ onZoomChange, resetKey: 'b' })} />);
    expect(lib.chart.dispatchAction).toHaveBeenLastCalledWith({ type: 'dataZoom', start: 0, end: 100 });

    lib.chart.dispatchAction.mockClear();
    rerender(<EChartsCanvas {...props({ onZoomChange, resetKey: 'b', resetToken: 1 })} />);
    expect(lib.chart.dispatchAction).toHaveBeenCalledTimes(1);
    expect(onZoomChange).toHaveBeenLastCalledWith(false);
  });

  it('tells which date the pointer is on, where it is in pixels and how wide the chart is', () => {
    const onHover = vi.fn();
    render(<EChartsCanvas {...props({ onHover })} />);
    const handler = (event: string) =>
      (lib.chart.on.mock.calls as unknown as [string, (e: unknown) => void][]).find(([name]) => name === event)?.[1];

    handler('updateAxisPointer')?.({ axesInfo: [{ axisDim: 'x', axisIndex: 0, value: 2 }] });

    expect(lib.chart.convertToPixel).toHaveBeenCalledWith({ xAxisIndex: 0 }, 2);
    expect(onHover).toHaveBeenLastCalledWith({ index: 2, x: 120, width: 600 });
  });

  it('tells when the pointer leaves the dates or the chart', () => {
    const onHover = vi.fn();
    render(<EChartsCanvas {...props({ onHover })} />);
    const handler = (event: string) =>
      (lib.chart.on.mock.calls as unknown as [string, (e: unknown) => void][]).find(([name]) => name === event)?.[1];

    handler('updateAxisPointer')?.({ axesInfo: [] });
    expect(onHover).toHaveBeenLastCalledWith(null);

    onHover.mockClear();
    handler('globalout')?.({});
    expect(onHover).toHaveBeenCalledWith(null);
  });

  it('is hidden from assistive technology: the wrapper around it carries the description', () => {
    const { container } = render(<EChartsCanvas {...props()} />);
    expect(container.firstElementChild).toHaveAttribute('aria-hidden', 'true');
  });
});
