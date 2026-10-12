import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { appliedCss } from '@/test/css';
import { buildEmptyDataOverview } from '@/test/fixtures';
import { createDataFake } from '@/test/dataFake';
import { openStep, ready, renderData } from './testing';

describe('Data & import: the page', () => {
  it('has the title, the subtitle and the Update data button', async () => {
    renderData();

    expect(await screen.findByRole('heading', { level: 1, name: 'Data & import' })).toBeInTheDocument();
    expect(screen.getByText(/Load the CSV folder, fetch what is missing from Dhan/)).toBeInTheDocument();
    await ready();
    expect(screen.getByRole('button', { name: 'Update data' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'More update options' })).toBeEnabled();
  });

  it('shows a loading state, then the page', async () => {
    renderData();

    expect(screen.getByRole('progressbar', { name: 'Loading data overview' })).toBeInTheDocument();
    expect(await ready()).toBeInTheDocument();
  });

  it('says in words when the overview cannot be loaded, and loads it on Retry', async () => {
    const fake = createDataFake();
    fake.failures.overview = true;
    renderData({ fake });

    expect(await screen.findByText('The data service is not available.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Update data' })).toBeDisabled();

    fake.failures.overview = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await ready()).toBeInTheDocument();
    expect(screen.queryByText('The data service is not available.')).not.toBeInTheDocument();
  });

  it('with no data yet, says where to put the CSV files and what to press', async () => {
    renderData({ fake: createDataFake({ overview: buildEmptyDataOverview(), coverage: [], jobs: [] }) });

    expect(
      await screen.findByText('No data yet — put your CSV files in data/Stocks_CSV and press Update data'),
    ).toBeInTheDocument();
    expect(within(await ready()).getByText('No bars stored yet')).toBeInTheDocument();
    expect(screen.queryByRole('grid', { name: 'Coverage' })).not.toBeInTheDocument();
    const csv = await openStep('CSV files');
    expect(within(csv).getByText('0 of 214 files loaded')).toBeInTheDocument();
    expect(within(csv).getByText('0 rows · 214 pending')).toBeInTheDocument();
  });

  it('shows the ribbon, the pipeline and the tabs on one screen, in that order, with no card for single facts', async () => {
    renderData();

    const ribbon = await ready();
    const pipeline = await screen.findByRole('region', { name: 'Pipeline' });
    const tabs = await screen.findByRole('tablist', { name: 'Data views' });
    const follows = (a: Element, b: Element) =>
      Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
    expect(follows(screen.getByRole('heading', { level: 1 }), ribbon)).toBe(true);
    expect(follows(ribbon, pipeline)).toBe(true);
    expect(follows(pipeline, tabs)).toBe(true);
    expect(screen.queryByRole('region', { name: 'Data freshness' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { level: 2, name: 'Pipeline' })).not.toBeInTheDocument();
  });

  it('fills the rest of the viewport from md: the page is a column, the open tab takes what is left', async () => {
    renderData();
    const panel = await screen.findByRole('tabpanel', { name: 'Coverage' });

    const own = appliedCss(panel);
    expect(own.base).toContain('display: flex');
    expect(own.responsive.map((r) => `${r.query} ${r.css}`).join(' ')).toMatch(/min-width:900px\) .*flex: 1 1 auto/);
    const page = appliedCss(panel.parentElement!);
    expect(page.responsive.map((r) => `${r.query} ${r.css}`).join(' ')).toMatch(/min-width:900px\) .*height: 100%/);
  });
});
