import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildDataOverview, buildDhanOverview, buildEodOverview, buildEodRun, ids } from '@/test/fixtures';
import { createDataFake } from '@/test/dataFake';
import { ready, renderData } from './testing';

const pill = async (name = /^Auto/) => {
  await ready();
  return screen.findByRole('button', { name });
};
/** Opens the pill's popover: the whole line about the automatic update. */
const line = async () => {
  await userEvent.click(await pill());
  return screen.findByRole('dialog', { name: 'Automatic update' });
};
const withEod = (eod: ReturnType<typeof buildEodOverview>, rest: Parameters<typeof buildDataOverview>[0] = {}) =>
  createDataFake({ overview: buildDataOverview({ eod, ...rest }) });

const notConnected = buildDhanOverview({
  canFetch: false,
  reason: 'Dhan is not connected for the Data account.',
  reasonCode: 'data.dhan_not_connected',
  account: { id: ids.main, name: 'Dhan · Main', status: 'notConnected' },
});

describe('Data & import: the automatic update in the ribbon', () => {
  it('shows when it runs and when it runs next in one short item', async () => {
    renderData();

    expect(await pill()).toHaveAccessibleName('Auto 15:50 · next Tue 13 Oct');
  });

  it('opens the whole line: when it runs, when it runs next and how the last automatic run ended', async () => {
    renderData();

    expect(await line()).toHaveTextContent(
      'Automatic update: Daily at 15:50 IST on trading days · next Tue 13 Oct 15:50 · last automatic run succeeded 12 Oct 15:52',
    );
  });

  it('sits in the ribbon, above the pipeline strip', async () => {
    renderData();

    const ribbon = await ready();
    const strip = await screen.findByRole('region', { name: 'Pipeline' });
    expect(ribbon).toContainElement(await pill());
    expect(ribbon.compareDocumentPosition(strip) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('shows the time of a schedule that is configured differently', async () => {
    renderData({ fake: withEod(buildEodOverview({ timeIst: '16:30', nextRunAt: '2026-10-13T16:30:00+05:30' })) });

    expect(await pill()).toHaveAccessibleName('Auto 16:30 · next Tue 13 Oct');
    expect(await line()).toHaveTextContent('Daily at 16:30 IST on trading days · next Tue 13 Oct 16:30');
  });

  it('with no run yet says so', async () => {
    renderData({ fake: withEod(buildEodOverview({ lastRun: null })) });

    expect(await line()).toHaveTextContent('· no automatic run yet');
  });

  it('leaves the next run out when none is planned', async () => {
    renderData({ fake: withEod(buildEodOverview({ nextRunAt: null })) });

    expect(await pill()).toHaveAccessibleName('Auto 15:50');
    const region = await line();
    expect(region).toHaveTextContent('Daily at 15:50 IST on trading days · last automatic run succeeded');
    expect(region).not.toHaveTextContent('next');
  });

  it('a failed run is said in the item itself, with the reason the API gave in the popover', async () => {
    const run = buildEodRun({ status: 'failed', message: 'Dhan refused the access token. (data.dhan_not_connected)' });
    renderData({ fake: withEod(buildEodOverview({ lastRun: run })) });

    expect(await pill()).toHaveAccessibleName('Auto 15:50 · next Tue 13 Oct · last run failed');
    const region = await line();
    expect(region).toHaveTextContent('last automatic run failed 12 Oct 15:52');
    expect(within(region).getByText('Dhan refused the access token. (data.dhan_not_connected)')).toBeInTheDocument();
    expect(within(region).getByTestId('ErrorOutlineOutlinedIcon')).toBeInTheDocument();
  });

  it('a failed run without a message points to the runs', async () => {
    renderData({ fake: withEod(buildEodOverview({ lastRun: buildEodRun({ status: 'failed', message: null }) })) });

    expect(await line()).toHaveTextContent('The reason is in Recent runs.');
  });

  it('a skipped run gives the reason and, when the Data account is not connected, a Reconnect link', async () => {
    const run = buildEodRun({
      status: 'skipped',
      finishedAt: '2026-10-12T15:50:00+05:30',
      message: 'The Data account is not connected.',
    });
    renderData({ fake: withEod(buildEodOverview({ lastRun: run }), { dhan: notConnected }) });

    expect(await pill()).toHaveAccessibleName('Auto 15:50 · next Tue 13 Oct · last run skipped');
    const region = await line();
    expect(region).toHaveTextContent('last automatic run skipped 12 Oct 15:50');
    expect(within(region).getByText(/The Data account is not connected\./)).toBeInTheDocument();
    expect(within(region).getByRole('link', { name: 'Reconnect' })).toHaveAttribute(
      'href',
      `/accounts?account=${ids.main}`,
    );
  });

  it('a skipped run for another reason has no Reconnect link', async () => {
    const run = buildEodRun({ status: 'skipped', message: 'An update was already running.' });
    renderData({ fake: withEod(buildEodOverview({ lastRun: run })) });

    const region = await line();
    expect(region).toHaveTextContent('An update was already running.');
    expect(within(region).queryByRole('link')).not.toBeInTheDocument();
  });

  it('a run in progress says it is running', async () => {
    const run = buildEodRun({ status: 'running', finishedAt: null });
    renderData({ fake: withEod(buildEodOverview({ lastRun: run })) });

    expect(await pill()).toHaveAccessibleName('Auto 15:50 · next Tue 13 Oct · running now');
    expect(await line()).toHaveTextContent('last automatic run is running 12 Oct 15:50');
  });

  it('when switched off says only that', async () => {
    renderData({ fake: withEod(buildEodOverview({ enabled: false, nextRunAt: null })) });

    expect(await pill()).toHaveAccessibleName('Auto off');
    const region = await line();
    expect(region).toHaveTextContent('Automatic update is switched off');
    expect(region).not.toHaveTextContent('Daily at');
    expect(within(region).getByTestId('PauseCircleOutlineOutlinedIcon')).toBeInTheDocument();
  });

  it('marks the outcome with an icon as well as a word', async () => {
    renderData();

    expect(within(await line()).getByTestId('CheckCircleOutlinedIcon')).toBeInTheDocument();
  });

  it('closes with Escape and gives the focus back to the item', async () => {
    renderData();
    await line();

    await userEvent.keyboard('{Escape}');

    expect(screen.queryByRole('dialog', { name: 'Automatic update' })).not.toBeInTheDocument();
    expect(await pill()).toHaveFocus();
  });
});
