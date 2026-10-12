import { buildActionsNotice, buildQualityNotice } from '@/test/actionFixtures';
import { buildDataNotice, buildRunFailedNotice } from '@/test/fixtures';
import { deriveDataNotifications } from './lib/deriveDataNotifications';

describe('deriveDataNotifications', () => {
  it('maps the API notices: stable id, severity, text, time and where Open leads', () => {
    const [failed, gaps] = deriveDataNotifications([buildRunFailedNotice(), buildDataNotice()]);

    expect(failed).toMatchObject({
      id: expect.stringMatching(/^data:run_failed:/) as string,
      kind: 'data_run_failed',
      severity: 'error',
      href: '/data',
      accountId: null,
      at: Date.parse('2026-10-12T15:51:00+05:30'),
    });
    expect(gaps).toMatchObject({
      id: 'data:gaps_open:1760266320:4',
      kind: 'data_gaps_open',
      severity: 'warn',
      text: '4 sessions have missing 1-minute bars',
      href: '/data?gaps=open',
    });
  });

  it('maps the failed quality checks to the Data quality screen, with the severity of the API', () => {
    const [failed, warned] = deriveDataNotifications([
      buildQualityNotice(),
      buildQualityNotice({ id: 'data:quality_failed:1760266320:1', severity: 'warn', text: '1 instrument fails' }),
    ]);

    expect(failed).toMatchObject({
      id: 'data:quality_failed:1760266320:3',
      kind: 'data_quality_failed',
      severity: 'error',
      href: '/quality',
      accountId: null,
      at: Date.parse('2026-10-12T15:53:00+05:30'),
    });
    expect(warned).toMatchObject({ kind: 'data_quality_failed', severity: 'warn', href: '/quality' });
  });

  it('maps the suggested corporate actions to their card on the Data screen', () => {
    const [pending] = deriveDataNotifications([buildActionsNotice()]);

    expect(pending).toMatchObject({
      id: 'data:corporate_actions_pending:2',
      kind: 'data_corporate_actions_pending',
      severity: 'warn',
      text: '2 suggested corporate actions wait for your decision',
      href: '/data?section=actions&actions=suggested',
      accountId: null,
    });
  });

  it('keeps an unknown severity as information and drops a kind this version does not know', () => {
    const list = deriveDataNotifications([
      buildDataNotice({ severity: 'odd', at: null }),
      buildDataNotice({ id: 'x', kind: 'something_new' }),
    ]);

    expect(list).toHaveLength(1);
    expect(list[0]).toMatchObject({ severity: 'info', at: null });
  });

  it('gives nothing for no notices', () => {
    expect(deriveDataNotifications([])).toEqual([]);
  });
});
