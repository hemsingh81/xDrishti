import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { basketCard, basketIds, membersGrid, renderBaskets } from './testing';

const dialog = (name: string) => screen.findByRole('dialog', { name });

async function openNew() {
  await userEvent.click(screen.getByRole('button', { name: 'New basket' }));
  return within(await dialog('New basket'));
}

describe('Basket editor: create', () => {
  it('creates a basket with a palette colour, a purpose and a description, and selects it', async () => {
    const { fake, router } = renderBaskets();
    await membersGrid();

    const form = await openNew();
    await userEvent.type(form.getByRole('textbox', { name: /Name/ }), '  Dividend payers ');
    await userEvent.selectOptions(form.getByRole('combobox', { name: 'Purpose' }), 'watch');
    await userEvent.click(form.getByRole('radio', { name: 'Rose' }));
    await userEvent.type(form.getByRole('textbox', { name: 'Description' }), 'Steady payers to watch');
    await userEvent.click(form.getByRole('button', { name: 'Create' }));

    expect(await screen.findByText('Basket created — add instruments to it.')).toBeInTheDocument();
    expect(fake.calls.create).toEqual([
      { name: 'Dividend payers', colour: '#f43f5e', purpose: 'watch', description: 'Steady payers to watch' },
    ]);
    expect(await screen.findByRole('heading', { level: 2, name: 'Dividend payers' })).toBeInTheDocument();
    expect(basketCard('Dividend payers')).toHaveAttribute('aria-current', 'true');
    expect(router.state.location.search).toContain(`basket=${fake.basket('Dividend payers')?.id ?? ''}`);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('accepts any hex colour', async () => {
    const { fake } = renderBaskets();
    await membersGrid();

    const form = await openNew();
    await userEvent.type(form.getByRole('textbox', { name: /Name/ }), 'Custom');
    const hex = form.getByRole('textbox', { name: 'Hex colour' });
    await userEvent.clear(hex);
    await userEvent.type(hex, '#123ABC');
    expect(form.queryByRole('radio', { checked: true })).not.toBeInTheDocument();
    await userEvent.click(form.getByRole('button', { name: 'Create' }));

    await waitFor(() => {
      expect(fake.calls.create).toEqual([expect.objectContaining({ colour: '#123abc' })]);
    });
  });

  it('checks the form before asking the server', async () => {
    const { fake } = renderBaskets();
    await membersGrid();

    const form = await openNew();
    await userEvent.click(form.getByRole('button', { name: 'Create' }));
    expect(form.getByText('Enter a name.')).toBeInTheDocument();

    await userEvent.type(form.getByRole('textbox', { name: /Name/ }), 'Fine');
    const hex = form.getByRole('textbox', { name: 'Hex colour' });
    await userEvent.clear(hex);
    await userEvent.type(hex, 'red');
    await userEvent.click(form.getByRole('button', { name: 'Create' }));

    expect(form.getByText('Use a hex colour such as #6366f1.')).toBeInTheDocument();
    expect(fake.calls.create).toHaveLength(0);
  });

  it('shows the server message on the name when it is taken, and keeps the dialog open', async () => {
    const { fake } = renderBaskets();
    await membersGrid();

    const form = await openNew();
    await userEvent.type(form.getByRole('textbox', { name: /Name/ }), 'mychoice');
    await userEvent.click(form.getByRole('button', { name: 'Create' }));

    expect(await form.findByText("A basket called 'mychoice' exists already.")).toBeInTheDocument();
    expect(form.getByRole('textbox', { name: /Name/ })).toBeInvalid();
    expect(fake.names()).toEqual(['MyChoice', 'MyLongTerm', 'Best stocks']);

    await userEvent.type(form.getByRole('textbox', { name: /Name/ }), '2');
    expect(form.queryByText("A basket called 'mychoice' exists already.")).not.toBeInTheDocument();
  });

  it('can be cancelled', async () => {
    const { fake } = renderBaskets();
    await membersGrid();

    const form = await openNew();
    await userEvent.click(form.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(fake.calls.create).toHaveLength(0);
  });
});

describe('Basket editor: edit and archive', () => {
  it('edits the shown basket', async () => {
    const { fake } = renderBaskets();
    await membersGrid();

    await userEvent.click(screen.getByRole('button', { name: 'Edit' }));
    const form = within(await dialog('Edit MyChoice'));
    const name = form.getByRole('textbox', { name: /Name/ });
    expect(name).toHaveValue('MyChoice');
    expect(form.getByRole('radio', { name: 'Indigo', checked: true })).toBeInTheDocument();
    await userEvent.clear(name);
    await userEvent.type(name, 'Core picks');
    await userEvent.click(form.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Basket saved.')).toBeInTheDocument();
    expect(await screen.findByRole('heading', { level: 2, name: 'Core picks' })).toBeInTheDocument();
    expect(fake.calls.update[0]).toMatchObject({
      id: basketIds.myChoice,
      body: { name: 'Core picks', purpose: 'trading' },
    });
    expect(basketCard('Core picks')).toBeInTheDocument();
  });

  it('shows a conflict in the dialog and keeps the entries', async () => {
    const { fake } = renderBaskets();
    await membersGrid();
    fake.failNext('update', 409, 'baskets.conflict', 'Changed meanwhile.');

    await userEvent.click(screen.getByRole('button', { name: 'Edit' }));
    const form = within(await dialog('Edit MyChoice'));
    await userEvent.type(form.getByRole('textbox', { name: /Name/ }), ' X');
    await userEvent.click(form.getByRole('button', { name: 'Save' }));

    expect(await form.findByRole('alert')).toHaveTextContent('changed somewhere else at the same moment');
    expect(form.getByRole('textbox', { name: /Name/ })).toHaveValue('MyChoice X');
  });

  it('archives after a confirmation and shows the next basket', async () => {
    const { fake, router } = renderBaskets();
    await membersGrid();

    await userEvent.click(screen.getByRole('button', { name: 'Archive' }));
    const confirm = within(await dialog('Archive MyChoice?'));
    expect(confirm.getByText(/members and history are kept/)).toBeInTheDocument();
    await userEvent.click(confirm.getByRole('button', { name: 'Archive' }));

    expect(await screen.findByText('MyChoice archived. Its history is kept.')).toBeInTheDocument();
    expect(fake.archivedNames()).toEqual(['MyChoice']);
    expect(await screen.findByRole('heading', { level: 2, name: 'MyLongTerm' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^MyChoice/ })).not.toBeInTheDocument();
    expect(router.state.location.search).toBe('');
  });

  it('keeps the basket when the confirmation is cancelled', async () => {
    const { fake } = renderBaskets();
    await membersGrid();

    await userEvent.click(screen.getByRole('button', { name: 'Archive' }));
    await userEvent.click(within(await dialog('Archive MyChoice?')).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(fake.calls.archive).toHaveLength(0);
  });

  it('says so when archiving fails', async () => {
    const { fake } = renderBaskets();
    await membersGrid();
    fake.failNext('archive', 404, 'baskets.not_found', 'The basket does not exist.');

    await userEvent.click(screen.getByRole('button', { name: 'Archive' }));
    await userEvent.click(within(await dialog('Archive MyChoice?')).getByRole('button', { name: 'Archive' }));

    expect(await screen.findByText('The basket does not exist.')).toBeInTheDocument();
    expect(await screen.findByRole('heading', { level: 2, name: 'MyChoice' })).toBeInTheDocument();
  });
});
