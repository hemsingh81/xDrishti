import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildSwingAccount, summaryOf, buildAccountDetail } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { http, HttpResponse } from 'msw';
import { server } from '@/test/server';
import { SearchButton } from './SearchButton';

function renderSearch() {
  server.use(
    http.get('/api/accounts', () =>
      HttpResponse.json([summaryOf(buildAccountDetail()), summaryOf(buildSwingAccount())]),
    ),
  );
  return renderRoutes([{ path: '*', element: <SearchButton /> }], { initialPath: '/' });
}

const trigger = () => screen.getByRole('button', { name: 'Search or jump to' });
const openByButton = async () => {
  await userEvent.click(trigger());
  return screen.findByRole('dialog', { name: 'Search or jump to' });
};
const optionNames = (dialog: HTMLElement) =>
  within(dialog)
    .getAllByRole('option')
    .map((o) => o.textContent);

describe('Search or jump to', () => {
  it('opens from the button with pages, accounts and actions', async () => {
    renderSearch();
    const dialog = await openByButton();

    expect(within(dialog).getByRole('combobox', { name: 'Search or jump to' })).toBeInTheDocument();
    expect(within(dialog).getByRole('group', { name: 'Pages' })).toBeInTheDocument();
    expect(within(dialog).getByRole('group', { name: 'Actions' })).toBeInTheDocument();
    expect(within(dialog).getByRole('option', { name: /^Plan review.*coming in P7/ })).toBeInTheDocument();
    expect(within(dialog).getByRole('option', { name: /Toggle dark mode/ })).toBeInTheDocument();
    await waitFor(() => {
      expect(within(dialog).getByRole('group', { name: 'Accounts' })).toBeInTheDocument();
    });
    expect(within(dialog).getByRole('option', { name: /Test Dhan Swing/ })).toBeInTheDocument();
  });

  it.each([
    ['Ctrl+K', '{Control>}k{/Control}'],
    ['Cmd+K', '{Meta>}k{/Meta}'],
  ])('opens with %s', async (_name, keys) => {
    renderSearch();
    await userEvent.keyboard(keys);
    expect(await screen.findByRole('dialog', { name: 'Search or jump to' })).toBeInTheDocument();
  });

  it('filters as you type and shows a message when nothing matches', async () => {
    renderSearch();
    const dialog = await openByButton();
    const box = within(dialog).getByRole('combobox');

    await userEvent.type(box, 'sett');
    expect(optionNames(dialog).some((n) => n.startsWith('Settings'))).toBe(true);
    expect(within(dialog).queryByRole('option', { name: /Plan review/ })).not.toBeInTheDocument();

    await userEvent.clear(box);
    await userEvent.type(box, 'zzzz');
    expect(within(dialog).queryAllByRole('option')).toHaveLength(0);
    expect(within(dialog).getByText('No matches')).toBeInTheDocument();
  });

  it('moves with the arrow keys (tracked by aria-activedescendant) and opens the choice with Enter', async () => {
    const { router } = renderSearch();
    const dialog = await openByButton();
    const box = within(dialog).getByRole('combobox');
    await userEvent.type(box, 'accounts');

    const options = within(dialog).getAllByRole('option');
    expect(options[0]).toHaveAttribute('aria-selected', 'true');
    expect(box).toHaveAttribute('aria-activedescendant', options[0]?.id);

    await userEvent.keyboard('{ArrowDown}');
    expect(options[1]).toHaveAttribute('aria-selected', 'true');
    expect(box).toHaveAttribute('aria-activedescendant', options[1]?.id);
    await userEvent.keyboard('{ArrowUp}{ArrowUp}');
    expect(options[0]).toHaveAttribute('aria-selected', 'true');

    await userEvent.keyboard('{Enter}');
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/accounts');
    });
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('opens an account on its own page when it is chosen', async () => {
    const { router } = renderSearch();
    const dialog = await openByButton();
    await userEvent.type(within(dialog).getByRole('combobox'), 'swing');
    await userEvent.click(await within(dialog).findByRole('option', { name: /Test Dhan Swing/ }));
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/accounts');
    });
    expect(router.state.location.search).toBe('?account=00000000-0000-4000-8000-000000000002');
  });

  it('closes on Escape and returns focus to the trigger', async () => {
    renderSearch();
    await openByButton();
    await userEvent.keyboard('{Escape}');
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(trigger()).toHaveFocus();
  });

  it('starts empty again when reopened', async () => {
    renderSearch();
    const dialog = await openByButton();
    await userEvent.type(within(dialog).getByRole('combobox'), 'zzzz');
    await userEvent.keyboard('{Escape}');
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    const again = await openByButton();
    expect(within(again).getByRole('combobox')).toHaveValue('');
    expect(within(again).getAllByRole('option').length).toBeGreaterThan(3);
  });

  it('toggles the colour mode from the actions', async () => {
    renderSearch();
    const dialog = await openByButton();
    await userEvent.click(within(dialog).getByRole('option', { name: /Toggle dark mode/ }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(localStorage.getItem('xd-color-mode')).toMatch(/light|dark/);
  });
});
