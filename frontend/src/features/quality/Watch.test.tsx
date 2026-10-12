import { screen } from '@testing-library/react';
import { createQualityFake } from '@/test/qualityFake';
import { renderQuality } from './testing';

vi.mock('./lib/polling', () => ({ qualityPollMs: 25 }));

afterEach(() => {
  vi.useRealTimers();
});

describe('Data quality: an automatic run is noticed on an open page', () => {
  it('reads the overview once a minute while idle and shows a run that started by itself', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const fake = createQualityFake({ autoAdvance: false });
    renderQuality({ fake });
    await screen.findByRole('region', { name: 'Quality summary' });
    const reads = fake.calls.overview;

    fake.startAutomatic();
    await vi.advanceTimersByTimeAsync(30_000);
    expect(fake.calls.overview).toBe(reads);
    expect(screen.queryByRole('progressbar', { name: 'Checking the data progress' })).not.toBeInTheDocument();

    await vi.advanceTimersByTimeAsync(31_000);
    expect(await screen.findByRole('progressbar', { name: 'Checking the data progress' })).toBeInTheDocument();
    expect(fake.calls.overview).toBeGreaterThan(reads);
  });
});
