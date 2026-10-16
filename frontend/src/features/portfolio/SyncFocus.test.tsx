import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildAccountDetail, buildConnection, summaryOf, withConnection } from '@/test/fixtures';
import { server } from '@/test/server';
import { renderPortfolio, twoPortfolioAccounts } from './testing';

describe('Portfolio: ?focus=sync (the bell\'s "rows dropped" items)', () => {
  it('takes the keyboard to Sync trades & ledger, once, and removes the parameter', async () => {
    const { router } = renderPortfolio('?focus=sync');

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Sync trades & ledger' })).toHaveFocus();
    });
    await waitFor(() => {
      expect(router.state.location.search).toBe('');
    });
    expect(screen.getByRole('region', { name: 'Trades and ledger from Dhan' })).toBeInTheDocument();
  });

  it('keeps the other parameters of the address', async () => {
    const { router } = renderPortfolio('?status=closed&focus=sync');

    await waitFor(() => {
      expect(router.state.location.search).toBe('?status=closed');
    });
  });

  it('takes the keyboard to the Account picker when all accounts are shown: a read belongs to one account', async () => {
    server.use(twoPortfolioAccounts());
    renderPortfolio('?focus=sync');

    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Account' })).toHaveFocus();
    });
  });

  it('never focuses a disabled button: with the account not connected the sync bar takes the focus', async () => {
    const expired = withConnection(buildAccountDetail(), buildConnection({ status: 'expired' }));
    server.use(http.get('/api/accounts', () => HttpResponse.json([summaryOf(expired)])));
    renderPortfolio('?focus=sync');
    const sync = await screen.findByRole('button', { name: 'Sync trades & ledger' });

    await waitFor(() => {
      expect(document.activeElement).toContainElement(sync);
    });
    expect(sync).not.toHaveFocus();
  });
});
