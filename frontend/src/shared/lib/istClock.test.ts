import { istClockParts, istDayLabel, istMinutesOfDay } from './istClock';

afterEach(() => {
  vi.unstubAllEnvs();
});

const noon = Date.parse('2026-10-13T10:00:00Z');

describe('India time readouts', () => {
  it.each(['UTC', 'America/Los_Angeles', 'Pacific/Kiritimati', 'Asia/Kolkata'])(
    'show 15:30 for 10:00 UTC when the process zone is %s',
    (zone) => {
      vi.stubEnv('TZ', zone);

      expect(istClockParts(noon)).toEqual({ hours: '15', minutes: '30', seconds: '00' });
      expect(istMinutesOfDay(noon)).toBe(15 * 60 + 30);
    },
  );

  it('proves the zone switch itself works (so the cases above are not vacuous)', () => {
    vi.stubEnv('TZ', 'America/Los_Angeles');
    expect(new Date(noon).getHours()).toBe(3);
  });

  it('pads to two digits and uses a 24-hour day (midnight is 00, not 24)', () => {
    // 18:35:05 UTC is 00:05:05 the next day in India.
    expect(istClockParts(Date.parse('2026-10-13T18:35:05Z'))).toEqual({ hours: '00', minutes: '05', seconds: '05' });
  });

  it('names the calendar day in India, not the viewer’s', () => {
    vi.stubEnv('TZ', 'America/Los_Angeles');
    // 20:00 UTC on the 13th is already 01:30 on the 14th in India.
    expect(istDayLabel(Date.parse('2026-10-13T20:00:00Z'))).toMatch(/Wednesday.*14 October.*2026/);
  });
});
