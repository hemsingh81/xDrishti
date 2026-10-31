import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildLimitsRow, buildLimitsView, execAccountId } from '@/test/executionFixtures';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { LimitsForm } from './components/LimitsForm';
import { recordWrites } from './testing';

pinClock('2026-10-30T04:00:00Z');
recordWrites();

const limits = (view = buildLimitsView({ inForce: buildLimitsRow(), complete: true, missing: [] })) => {
  server.use(http.get('/api/execution/limits', () => HttpResponse.json(view)));
};
const show = () => renderWithProviders(<LimitsForm accountId={execAccountId} />);
const clearType = async (label: string, value: string) => {
  const field = await screen.findByLabelText(label);
  await userEvent.clear(field);
  await userEvent.type(field, value);
};

describe('Limits form', () => {
  it('says an empty sheet allows nothing', async () => {
    limits(buildLimitsView());
    show();
    expect(await screen.findByText('Empty: nothing may be sent automatically')).toBeInTheDocument();
  });

  it('shows the sheet in force beside the form', async () => {
    limits();
    show();
    const sheet = await screen.findByRole('region', { name: 'Sheet in force' });
    expect(within(sheet).getByText('150000')).toBeInTheDocument();
    expect(within(sheet).getByText('TATAPOWER')).toBeInTheDocument();
    expect(within(sheet).getByText('09:30 to 14:30')).toBeInTheDocument();
  });

  it('marks a raise, asks the password and sends it; a pure lowering needs none', async () => {
    limits();
    let body: Record<string, unknown> = {};
    server.use(
      http.put(`/api/execution/limits/${execAccountId}`, async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          buildLimitsView({
            inForce: buildLimitsRow(),
            pending: buildLimitsRow({ id: 5, maxOrdersPerDay: 8, effectiveFrom: '2026-11-02T03:30:00Z' }),
            complete: true,
            missing: [],
          }),
        );
      }),
    );
    show();
    expect(await screen.findByText(/only lowers or keeps risk: it needs no password/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/Password/)).not.toBeInTheDocument();

    await clearType('Orders a day', '8');
    expect(screen.getByText('Raises risk')).toBeInTheDocument();
    expect(
      screen.getByText(/needs your password and takes effect at 09:00 IST of the next trading day/),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save limits' })).toBeDisabled();

    await userEvent.type(screen.getByLabelText(/Reason/), 'more room');
    await userEvent.type(screen.getByLabelText(/Password/), 'pw');
    await userEvent.click(screen.getByRole('button', { name: 'Save limits' }));
    expect(await screen.findByText(/Saved\. Effective from/)).toHaveTextContent('2 Nov');
    expect(body).toMatchObject({ maxOrdersPerDay: 8, reason: 'more room', password: 'pw', allowList: ['TATAPOWER'] });
  });

  it('treats a lower cap as immediate and sends no password', async () => {
    limits();
    let body: Record<string, unknown> = {};
    server.use(
      http.put(`/api/execution/limits/${execAccountId}`, async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          buildLimitsView({ inForce: buildLimitsRow({ maxOrdersPerDay: 4 }), complete: true, missing: [] }),
        );
      }),
    );
    show();
    await clearType('Orders a day', '4');
    expect(screen.queryByText('Raises risk')).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText(/Reason/), 'less');
    await userEvent.click(screen.getByRole('button', { name: 'Save limits' }));
    expect(await screen.findByText(/Saved\./)).toBeInTheDocument();
    expect(body).toMatchObject({ maxOrdersPerDay: 4, password: null });
  });

  it('refuses a value above a ceiling before it is sent', async () => {
    limits();
    show();
    await clearType('Orders a day', '25');
    expect(screen.getByText('Above the ceiling')).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText(/Reason/), 'x');
    expect(screen.getByRole('button', { name: 'Save limits' })).toBeDisabled();
  });

  it('shows the server field errors next to the field', async () => {
    limits();
    server.use(
      http.put(`/api/execution/limits/${execAccountId}`, () =>
        HttpResponse.json(
          { title: 'Invalid', errors: { maxLossPerDay: ['Must be below the notional cap'] } },
          { status: 400 },
        ),
      ),
    );
    show();
    await clearType('Worst-case loss a day (Rs)', '1000');
    await userEvent.type(screen.getByLabelText(/Reason/), 'x');
    await userEvent.click(screen.getByRole('button', { name: 'Save limits' }));
    expect(await screen.findByText('Must be below the notional cap')).toBeInTheDocument();
  });
});
