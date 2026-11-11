import type { components } from './schema';

type Schemas = components['schemas'];

/** A stored system basket's file: its state, last checks and last fetches (BK-01). */
export type BasketSource = Schemas['BasketSourceView'];
export type BasketSourceSummary = Schemas['BasketSourceSummary'];
export type BasketFetchRun = Schemas['BasketFetchRun'];
export type DerivedRun = Schemas['DerivedRun'];
export type BasketVersionPage = Schemas['BasketVersionPage'];
export type BasketVersionSummary = Schemas['BasketVersionSummary'];
export type BasketVersionDetail = Schemas['BasketVersionDetail'];
/** Which owner-act switches are on (BK-04). */
export type SystemBasketSettings = Schemas['SystemBasketSettingsView'];
