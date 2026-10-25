import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { accountA, accountB } from '@/test/planFixtures';
import type { ReviewAccount } from './api/planQueries';
import { TicketChecklist } from './components/TicketChecklist';
import { standingLine } from './lib/standings';

const c = (code: string, standing: string, outcome = 'pass'): ReviewAccount['checks'][number] => ({
  code,
  outcome,
  standing,
  detail: `detail of ${code}`,
});
const account = (accountId: string, checks: ReviewAccount['checks']): ReviewAccount => ({
  accountId,
  mode: 'paper',
  quantity: 10,
  effectiveQuantity: 10,
  canApprove: false,
  checks,
});
const names = { [accountA]: 'Main paper', [accountB]: 'Swing paper', 'c-3': 'Test paper', 'c-4': 'Bare paper' };

describe('TicketChecklist standings', () => {
  const accounts = [
    account(
      accountA,
      Array.from({ length: 12 }, (_, i) => c(`ok${String(i)}`, 'passed')),
    ),
    account(accountB, [c('liq', 'warned', 'warn'), c('dq', 'needsconfirmation', 'unavailable'), c('fine', 'passed')]),
    account('c-3', [c('ban', 'refused', 'fail'), c('fine', 'passed')]),
    account('c-4', []),
  ];

  it('prints one standing line per account with the mode and quantity', () => {
    render(<TicketChecklist accounts={accounts} names={names} />);

    expect(screen.getByText(/Main paper: 12 checks, 12 passed/)).toBeInTheDocument();
    expect(screen.getByText(/Swing paper: 1 warned, 1 needs confirmation/)).toBeInTheDocument();
    expect(screen.getByText(/Test paper: refused, cannot be overridden/)).toBeInTheDocument();
    expect(screen.getByText(/Bare paper: no checklist was stored/)).toBeInTheDocument();
    expect(screen.getAllByText('Mode: paper')).toHaveLength(4);
    expect(screen.getByLabelText('Checks of Swing paper')).toHaveTextContent('detail of liq');
    expect(screen.queryByLabelText('Checks of Main paper')).not.toBeInTheDocument();
  });

  it('lists every check with its standing as text on request', async () => {
    render(<TicketChecklist accounts={accounts} names={names} />);

    await userEvent.click(screen.getByRole('button', { name: 'Show all checks of Swing paper' }));

    expect(screen.getByLabelText('Checks of Swing paper')).toHaveTextContent('fine: pass, Passed.');
    expect(screen.getByRole('button', { name: 'Show fewer checks of Swing paper' })).toHaveAttribute(
      'aria-expanded',
      'true',
    );
  });

  it('draws nothing, not even a heading, for a ticket with no accounts', () => {
    const { container } = render(<TicketChecklist accounts={[]} names={{}} />);

    expect(container).toBeEmptyDOMElement();
  });

  it('says a standing it does not know instead of hiding it', () => {
    expect(standingLine(account(accountA, [c('x', 'odd', 'pass')]))).toBe('1 not passed');
  });
});
