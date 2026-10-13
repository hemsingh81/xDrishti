/** A slice of an allocation donut. `color` is a slot of the categorical palette (0-7, 8 = "Other"). */
export interface CategorySlice {
  /** Stable id: keeps the legend state when the data changes. */
  id: string;
  name: string;
  value: number;
  color: number;
}

/** One row of a stacked bar (a sector, an account). */
export interface StackCategory {
  id: string;
  name: string;
}

/** One colour of a stacked bar: its value in every category, in the order of `categories`. */
export interface StackSeries {
  id: string;
  name: string;
  color: number;
  values: readonly number[];
}

/** What a categorical chart draws: a donut of slices, or bars of categories split into stacks. */
export type CategoryData =
  | { kind: 'donut'; slices: readonly CategorySlice[] }
  | { kind: 'stacked'; categories: readonly StackCategory[]; stacks: readonly StackSeries[] };

/** The element under the pointer: the slice (donut) or the category row (stacked bar), and where the pointer is. */
export interface CategoryHover {
  index: number;
  x: number;
  y: number;
  /** Width of the chart in px: the tooltip sits on the roomier side of the pointer. */
  width: number;
}
