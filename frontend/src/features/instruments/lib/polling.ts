/** While a refresh runs (or has just been started) the status is read this often. */
export const refreshPollMs = 1500;

/** After starting, keep polling this long even if the run is not visible yet (queued for the worker). */
export const startGraceMs = 20_000;
