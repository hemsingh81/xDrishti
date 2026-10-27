import { Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { proposalsQuery } from '../api/proposalApi';
import { words } from '../lib/wording';
import { ProposalCard } from './ProposalCard';

/** The suggestions inbox (spec P9-08 AC 14): pending first, then the decided and expired ones. */
export function SuggestionsPage() {
  const proposals = useQuery(proposalsQuery);
  if (proposals.isPending) return <LoadingState label="Loading the suggestions…" />;
  if (proposals.isError) return <ErrorState error={proposals.error} />;
  const rank = (state: string) => (state === 'pending' ? 0 : 1);
  const sorted = [...proposals.data].sort((a, b) => rank(a.state) - rank(b.state) || b.id - a.id);

  return (
    <>
      <PageHeader title={words.title} />
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2, maxWidth: 720 }}>
        {words.intro}
      </Typography>
      {sorted.length === 0 ? (
        <Typography>{words.empty}</Typography>
      ) : (
        <Stack spacing={2} sx={{ maxWidth: 840 }}>
          {sorted.map((p) => (
            <ProposalCard key={p.id} proposal={p} />
          ))}
        </Stack>
      )}
    </>
  );
}
