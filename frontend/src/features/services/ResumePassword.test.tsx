import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createServicesFake } from '@/test/servicesFake';
import { buildMasterService, buildServiceView } from '@/test/servicesFixtures';
import { chooseAction, renderServices, serviceCard } from './testing';

vi.mock('./lib/polling', () => ({ servicesRunningPollMs: 25, servicesIdlePollMs: 60_000 }));

const eod = 'End-of-day data run';
const refresh = 'Cell statistics refresh';

const services = () => [
  buildServiceView({ enabled: false, status: 'paused', nextRun: null }),
  buildServiceView({
    id: 'cells-refresh',
    name: refresh,
    enabled: false,
    status: 'paused',
    nextRun: null,
    resumeNeedsPassword: true,
  }),
  buildMasterService(),
];

describe('Services: resuming a service that needs the owner’s password', () => {
  it('resumes an ordinary service at once, with no dialog and no body', async () => {
    const fake = createServicesFake({ services: services() });
    renderServices({ fake });
    await chooseAction(eod, 'Resume');

    expect(await screen.findByText(`${eod} resumed.`)).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(fake.calls.resumePasswords).toEqual([]);
  });

  it('says on the paused row that it needs the password, and not on an ordinary row', async () => {
    renderServices({ fake: createServicesFake({ services: services() }) });

    const row = await serviceCard(refresh);

    expect(within(row).getByText('Needs your password to resume.')).toBeInTheDocument();
    expect(within(await serviceCard(eod)).queryByText(/needs your password/i)).not.toBeInTheDocument();
  });

  it('asks for the password in a dialog with no reason and sends it in the body', async () => {
    const fake = createServicesFake({ services: services() });
    renderServices({ fake });
    await chooseAction(refresh, 'Resume');

    const dialog = await screen.findByRole('dialog', { name: `Resume ${refresh}` });
    expect(within(dialog).queryByLabelText(/reason/i)).not.toBeInTheDocument();
    expect(fake.calls.resumes).toEqual([]);
    await userEvent.type(within(dialog).getByLabelText(/password/i), 'right-password');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Resume' }));

    expect(await screen.findByText(`${refresh} resumed.`)).toBeInTheDocument();
    expect(fake.calls.resumePasswords).toEqual(['right-password']);
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('shows a wrong password in the dialog and keeps the session and the dialog', async () => {
    const fake = createServicesFake({ services: services() });
    renderServices({ fake });
    await chooseAction(refresh, 'Resume');
    const dialog = await screen.findByRole('dialog', { name: `Resume ${refresh}` });

    await userEvent.type(within(dialog).getByLabelText(/password/i), 'nope');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Resume' }));

    expect(await within(dialog).findByText('The password is not right.')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: `Resume ${refresh}` })).toBeInTheDocument();
    expect(fake.calls.resumePasswords).toEqual(['nope']);
  });
});
