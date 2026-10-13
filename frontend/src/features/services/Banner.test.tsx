import { screen, within } from '@testing-library/react';
import { createServicesFake } from '@/test/servicesFake';
import { buildMasterService, buildServiceIssue, buildServiceView, serviceIds } from '@/test/servicesFixtures';
import { renderServices } from './testing';

const banner = () => screen.findByRole('list', { name: 'Schedule check findings' });

describe('Services: the schedule checks banner', () => {
  it('says all is well when no service has a finding', async () => {
    renderServices();

    expect(
      await screen.findByText('Schedule checks passed — dependencies and timing are consistent.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Schedule check findings' })).not.toBeInTheDocument();
  });

  it('lists a warning with its service and field, and counts it in the title', async () => {
    renderServices({
      fake: createServicesFake({
        services: [buildServiceView({ issues: [buildServiceIssue()] }), buildMasterService()],
      }),
    });

    const list = await banner();
    expect(screen.getByText('Schedule checks: 1 warning')).toBeInTheDocument();
    const [item] = within(list).getAllByRole('listitem');
    expect(item).toHaveTextContent(
      'End-of-day data run · Time · Warning: The end-of-day fetch before 15:35 may miss the last bars of the session.',
    );
    expect(screen.getByTestId('WarningAmberOutlinedIcon')).toBeInTheDocument();
  });

  it('puts errors first and counts errors and warnings', async () => {
    renderServices({
      fake: createServicesFake({
        services: [
          buildServiceView({ issues: [buildServiceIssue()] }),
          buildMasterService({
            issues: [
              buildServiceIssue({
                serviceId: serviceIds.master,
                field: 'schedule.after',
                severity: 'error',
                code: 'services.circular',
                message: 'The run-after chain is circular.',
              }),
            ],
          }),
        ],
      }),
    });

    const list = await banner();
    expect(screen.getByText('Schedule checks: 1 error, 1 warning')).toBeInTheDocument();
    const items = within(list).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent(
      'Instrument master refresh · Run after · Error: The run-after chain is circular.',
    );
    expect(items[1]).toHaveTextContent('End-of-day data run · Time · Warning:');
    expect(screen.getByTestId('ErrorOutlineIcon')).toBeInTheDocument();
  });

  it('names a finding that is reported on a dependent service once, however many services carry it', async () => {
    const shared = buildServiceIssue({
      serviceId: serviceIds.master,
      relatedServiceId: serviceIds.eod,
      field: 'schedule.after',
    });
    renderServices({
      fake: createServicesFake({
        services: [buildServiceView({ issues: [shared] }), buildMasterService({ issues: [shared] })],
      }),
    });

    const list = await banner();
    expect(within(list).getAllByRole('listitem')).toHaveLength(1);
    expect(within(list).getByText('Instrument master refresh')).toBeInTheDocument();
  });

  it('names a parameter by its label', async () => {
    renderServices({
      fake: createServicesFake({
        services: [
          buildServiceView({
            issues: [
              buildServiceIssue({ field: 'parameters.lookbackDays', severity: 'error', message: 'Out of range.' }),
            ],
          }),
        ],
      }),
    });

    expect(await screen.findByText(/Gap look-back · Error: Out of range\./)).toBeInTheDocument();
  });
});
