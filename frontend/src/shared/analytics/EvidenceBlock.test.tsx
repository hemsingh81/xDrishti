import { screen, within } from '@testing-library/react';
import { renderKit, usesGoodColour } from '@/test/analyticsKit';
import { EvidenceBlock } from './EvidenceBlock';

const range = { from: '2024-04-01', to: '2026-04-30' };

describe('EvidenceBlock', () => {
  it('is a named section with n, range and basis as labelled terms', () => {
    renderKit(<EvidenceBlock n={1284} range={range} basis="gross" />);
    const block = screen.getByRole('region', { name: 'Evidence' });
    expect(within(block).getByText('Sample').nextElementSibling).toHaveTextContent('n 1,284');
    expect(within(block).getByText('Range').nextElementSibling).toHaveTextContent('1 Apr 2024 to 30 Apr 2026');
    expect(within(block).getByText('Basis').nextElementSibling).toHaveTextContent('Gross R (before costs)');
    expect(within(block).queryByText('Provisional')).not.toBeInTheDocument();
  });

  it('takes a landmark name so two blocks on one page can be told apart', () => {
    renderKit(
      <>
        <EvidenceBlock n={1} range={range} basis="gross" label="Evidence for the heatmap" />
        <EvidenceBlock n={2} range={range} basis="gross" label="Evidence for the table" />
      </>,
    );
    expect(screen.getByRole('region', { name: 'Evidence for the heatmap' })).toHaveTextContent('n 1');
    expect(screen.getByRole('region', { name: 'Evidence for the table' })).toHaveTextContent('n 2');
  });

  it('keeps two identical limit strings as two items', () => {
    renderKit(<EvidenceBlock n={1} range={range} basis="gross" limits={['In-sample', 'In-sample']} />);
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
  });

  it('says net only when the basis is net', () => {
    renderKit(<EvidenceBlock n={10} range={range} basis="net" />);
    expect(screen.getByText('Net R (after the stored charge sheet)')).toBeInTheDocument();
    expect(screen.queryByText(/Gross/)).not.toBeInTheDocument();
  });

  it('says what it does not know instead of leaving a gap or a zero', () => {
    renderKit(<EvidenceBlock n={null} range={null} basis="unknown" />);
    expect(screen.getByText('n not reported')).toBeInTheDocument();
    expect(screen.getByText('range not stated')).toBeInTheDocument();
    expect(screen.getByText('Basis not stated')).toBeInTheDocument();
  });

  it('keeps a real n of zero as zero', () => {
    renderKit(<EvidenceBlock n={0} range={range} basis="gross" />);
    expect(screen.getByText('n 0')).toBeInTheDocument();
  });

  it('marks provisional with the word and an icon, not in the good colour', () => {
    const { container } = renderKit(<EvidenceBlock n={5} range={range} basis="gross" provisional />);
    expect(screen.getByText('Provisional')).toBeInTheDocument();
    expect(container.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
    expect(usesGoodColour(container)).toBe(false);
  });

  it('lists the data used and the limits the server sent, as text', () => {
    renderKit(
      <EvidenceBlock
        n={5}
        range={range}
        basis="gross"
        population={['run-17', 'run-18']}
        limits={['About 2.1 years of replayed history', '<b>No hold-out</b>']}
      />,
    );
    expect(screen.getByText('run-17, run-18')).toBeInTheDocument();
    const limits = screen.getByRole('list', { name: 'Limits' });
    expect(
      within(limits)
        .getAllByRole('listitem')
        .map((li) => li.textContent),
    ).toEqual(['About 2.1 years of replayed history', '<b>No hold-out</b>']);
    expect(limits.querySelector('b')).toBeNull();
  });

  it('omits the data-used line and the limits list when there are none', () => {
    renderKit(<EvidenceBlock n={5} range={range} basis="gross" population={[]} limits={[]} />);
    expect(screen.queryByText(/Data used/)).not.toBeInTheDocument();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
  });

  it('has a compact one-line form with the same facts', () => {
    renderKit(<EvidenceBlock compact n={1284} range={range} basis="net" provisional />);
    expect(
      screen.getByText('n 1,284 · 1 Apr 2024 to 30 Apr 2026 · Net R (after the stored charge sheet) · Provisional'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('region')).not.toBeInTheDocument();
  });
});
