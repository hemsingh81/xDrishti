import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { appliedCss } from '@/test/css';
import { stubPhoneWidth } from '@/test/matchMedia';
import { csvFile, stubUploads } from '@/test/multipart';
import { renderRoutes } from '@/test/render';
import { PortfolioPage } from './components/PortfolioPage';
import { renderPortfolio, serveTrades, tradesPanel, warmTrades } from './testing';

stubUploads();

beforeAll(warmTrades, 60_000);

async function start(from: string) {
  const view = renderRoutes(
    [
      { path: '/portfolio', element: <PortfolioPage /> },
      { path: '/other', element: <p>Other screen</p> },
    ],
    { initialPath: '/other' },
  );
  await screen.findByText('Other screen');
  await act(async () => {
    await view.router.navigate(from);
  });
  return view;
}

const gone = () =>
  waitFor(() => {
    expect(screen.queryByRole('dialog', { name: 'Trades' })).not.toBeInTheDocument();
  });

describe('Trades panel', () => {
  it('opens from the Trades button on the Portfolio screen and keeps its tab in the URL', async () => {
    serveTrades();
    const { router } = renderPortfolio();

    await userEvent.click(await screen.findByRole('button', { name: 'Trades' }));

    const panel = within(await tradesPanel());
    expect(router.state.location.search).toBe('?trades=import');
    expect(panel.getByRole('heading', { name: 'Trades' })).toBeInTheDocument();
    expect(panel.getByRole('tab', { name: 'Import CSV', selected: true })).toBeInTheDocument();
    expect(panel.getByRole('tab', { name: 'History', selected: false })).toBeInTheDocument();
    expect(panel.getByRole('button', { name: 'Add trade' })).toBeInTheDocument();
    expect(panel.getByText(/never changes a lot by itself/)).toBeInTheDocument();
  });

  it('opens on the tab named in the URL, History included', async () => {
    serveTrades();
    renderPortfolio('?trades=history');

    const panel = within(await tradesPanel());
    expect(await panel.findByRole('tab', { name: 'History', selected: true })).toBeInTheDocument();
    expect(await panel.findByText(/No tradebook has been imported yet/)).toBeInTheDocument();
  });

  it('ignores a trades parameter it does not know', async () => {
    serveTrades();
    renderPortfolio('?trades=nonsense');

    expect(await screen.findByRole('button', { name: 'Trades' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Trades' })).not.toBeInTheDocument();
  });

  it('moves between the tabs with the arrow keys, Home and End, and keeps a half-done import', async () => {
    serveTrades();
    const { router } = renderPortfolio('?trades=import');
    const panel = within(await tradesPanel());
    await userEvent
      .setup({ applyAccept: false })
      .upload(panel.getByLabelText('Tradebook file (CSV)'), csvFile('d,s\n', 'half-done.csv'));
    const first = panel.getByRole('tab', { name: 'Import CSV' });
    expect(first).toHaveAttribute('tabindex', '0');
    expect(panel.getByRole('tab', { name: 'History' })).toHaveAttribute('tabindex', '-1');
    first.focus();

    await userEvent.keyboard('{ArrowRight}');
    expect(panel.getByRole('tab', { name: 'History' })).toHaveFocus();
    expect(router.state.location.search).toBe('?trades=history');
    expect(await panel.findByRole('tabpanel', { name: 'History' })).toBeInTheDocument();

    await userEvent.keyboard('{Home}');
    expect(panel.getByRole('tab', { name: 'Import CSV' })).toHaveFocus();
    await userEvent.keyboard('{End}');
    expect(panel.getByRole('tab', { name: 'History' })).toHaveFocus();
    await userEvent.keyboard('{ArrowLeft}');
    expect(panel.getByText('half-done.csv')).toBeVisible();
  });

  it('closes with the close button and takes the parameter out of the URL', async () => {
    serveTrades();
    const { router } = renderPortfolio('?trades=history&status=all');
    const panel = within(await tradesPanel());

    await userEvent.click(panel.getByRole('button', { name: 'Close trades' }));

    await gone();
    expect(router.state.location.search).toBe('?status=all');
  });

  it('takes one Back to leave the screen after opening and closing it from the button', async () => {
    serveTrades();
    const { router } = await start('/portfolio');
    await userEvent.click(await screen.findByRole('button', { name: 'Trades' }));
    const panel = within(await tradesPanel());
    await userEvent.click(panel.getByRole('tab', { name: 'History' }));
    expect(router.state.location.search).toBe('?trades=history');
    await userEvent.click(panel.getByRole('button', { name: 'Close trades' }));

    await gone();
    expect(router.state.location.search).toBe('');
    await act(async () => {
      await router.navigate(-1);
    });
    expect(router.state.location.pathname).toBe('/other');
  });

  it('closes with Back, as the close button would', async () => {
    serveTrades();
    const { router } = await start('/portfolio');
    await userEvent.click(await screen.findByRole('button', { name: 'Trades' }));
    await tradesPanel();

    await act(async () => {
      await router.navigate(-1);
    });

    await gone();
    expect(router.state.location.pathname).toBe('/portfolio');
    expect(router.state.location.search).toBe('');
  });

  it('closes a panel that a link opened by replacing its entry, so one Back leaves the screen', async () => {
    serveTrades();
    const { router } = await start('/portfolio?trades=import');
    const panel = within(await tradesPanel());

    await userEvent.click(panel.getByRole('button', { name: 'Close trades' }));

    await gone();
    expect(router.state.location.search).toBe('');
    await act(async () => {
      await router.navigate(-1);
    });
    expect(router.state.location.pathname).toBe('/other');
  });

  it('closes with Escape', async () => {
    serveTrades();
    renderPortfolio('?trades=import');
    await tradesPanel();

    await userEvent.keyboard('{Escape}');

    await gone();
  });

  it('fills the screen on a phone and the header button is reachable', async () => {
    stubPhoneWidth();
    serveTrades();
    renderPortfolio('?trades=import');

    const panel = await tradesPanel();

    const paper = panel.closest('.MuiDrawer-paper') ?? panel;
    const width = appliedCss(paper);
    expect(`${width.base} ${width.responsive.map((rule) => rule.css).join(' ')}`).toMatch(/width: 100%/);
    expect(within(panel).getByRole('button', { name: 'Add trade' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Close trades' })).toBeInTheDocument();
  });
});
