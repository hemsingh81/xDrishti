import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { LoadingState } from '@/shared/ui';
import { LoadingAnnouncer } from './LoadingAnnouncer';

function Panel({ name, release }: { name: string; release: Promise<void> }) {
  const query = useQuery({
    queryKey: ['announcer', name],
    queryFn: async () => {
      await release;
      return name;
    },
  });
  return query.isPending ? <LoadingState label={`Loading ${name}…`} /> : <p>{query.data}</p>;
}

describe('LoadingAnnouncer', () => {
  it('says "Loading…" once for several loading panels, then "Loaded", while the panels stay silent', async () => {
    let open: () => void = () => undefined;
    const release = new Promise<void>((resolve) => {
      open = resolve;
    });
    const client = new QueryClient();
    render(
      <QueryClientProvider client={client}>
        <LoadingAnnouncer />
        <Panel name="runs" release={release} />
        <Panel name="figures" release={release} />
        <Panel name="insights" release={release} />
      </QueryClientProvider>,
    );

    const polite = () =>
      screen.getAllByRole('status').filter((region) => region.getAttribute('aria-live') === 'polite');
    await waitFor(() => {
      expect(polite()).toHaveLength(1);
      expect(polite()[0]).toHaveTextContent('Loading…');
    });
    // The three panels still show their words (a reader moving through the page finds them) but none announces.
    expect(screen.getByText('Loading runs…')).toBeInTheDocument();
    expect(screen.getAllByRole('status').filter((region) => region.getAttribute('aria-live') === 'off')).toHaveLength(
      3,
    );

    open();
    await waitFor(() => {
      expect(polite()[0]).toHaveTextContent('Loaded');
    });
    expect(screen.getByText('insights')).toBeInTheDocument();
  });

  it('stays silent when nothing is loading', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <LoadingAnnouncer />
      </QueryClientProvider>,
    );
    expect(screen.getByRole('status')).toBeEmptyDOMElement();
  });
});
