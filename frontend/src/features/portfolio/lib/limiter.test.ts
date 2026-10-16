import { createLimiter } from './limiter';

describe('createLimiter', () => {
  it('never has more than max tasks in flight, and runs them all', async () => {
    const limit = createLimiter(4);
    let running = 0;
    let peak = 0;
    const task = async (n: number) => {
      running += 1;
      peak = Math.max(peak, running);
      await new Promise((resolve) => setTimeout(resolve, 1 + (n % 3)));
      running -= 1;
      return n;
    };

    const results = await Promise.all(Array.from({ length: 30 }, (_, n) => limit(() => task(n))));

    expect(results).toEqual(Array.from({ length: 30 }, (_, n) => n));
    expect(peak).toBe(4);
  });

  it('hands a finished task its slot to the next one at once, also when a task fails', async () => {
    const limit = createLimiter(1);
    const order: string[] = [];
    const failing = limit(() => {
      order.push('a');
      return Promise.reject(new Error('x'));
    });
    const next = limit(() => {
      order.push('b');
      return Promise.resolve('b');
    });

    await expect(failing).rejects.toThrow('x');
    await expect(next).resolves.toBe('b');
    expect(order).toEqual(['a', 'b']);
    // And the limiter is empty again: a later task starts at once.
    await expect(limit(() => Promise.resolve('c'))).resolves.toBe('c');
  });
});
