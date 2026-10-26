/** One bar of a price chart: `t` is the epoch seconds of its start. */
export interface PriceBar {
  t: number;
  o: number;
  h: number;
  l: number;
  c: number;
  v: number;
}

export type PriceLineKind = 'entry' | 'stop' | 'target';

/** A horizontal level with its label on the price axis ("Stop 415.00"). */
export interface PriceLine {
  id: string;
  kind: PriceLineKind;
  price: number;
  title: string;
}

export type PriceEventKind = 'entry' | 'add' | 'exit';

/** Something that happened at a price: an entry, an add, an exit. `announce` is what a screen reader hears when it is focused. */
export interface PriceEvent {
  id: string;
  /** Epoch seconds of the minute it happened in. */
  t: number;
  price: number;
  kind: PriceEventKind;
  side: 'BUY' | 'SELL';
  /** The marker text on the chart: "Exit 100%". */
  text: string;
  announce: string;
}

export type MarkerShape = 'arrowUp' | 'arrowDown' | 'circle' | 'square';

/** An event placed on a bar of the chart. */
export interface PriceMarker {
  id: string;
  time: number;
  position: 'aboveBar' | 'belowBar';
  shape: MarkerShape;
  text: string;
}

/** A candle as the library takes it. */
export interface Candle {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
}

/** Everything the chart paints with, from the theme at run time. */
export interface PriceChartColors {
  background: string;
  text: string;
  grid: string;
  border: string;
  up: string;
  down: string;
  stop: string;
  target: string;
  entry: string;
}

/** Where the crosshair is put by the event buttons. */
export interface PriceFocus {
  time: number;
  price: number;
}
