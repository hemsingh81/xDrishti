/** Runs tasks with at most `max` of them in flight; the rest wait their turn. A task must not wait for another task of the same limiter. */
export type Limiter = <T>(task: () => Promise<T>) => Promise<T>;

export function createLimiter(max: number): Limiter {
  let running = 0;
  const waiting: (() => void)[] = [];
  /** The slot of a finished task goes straight to the next waiting one (it stays counted), so `max` is never exceeded. */
  const release = () => {
    const next = waiting.shift();
    if (next) next();
    else running -= 1;
  };
  return async <T>(task: () => Promise<T>): Promise<T> => {
    if (running >= max) await new Promise<void>((resolve) => waiting.push(resolve));
    else running += 1;
    try {
      return await task();
    } finally {
      release();
    }
  };
}
