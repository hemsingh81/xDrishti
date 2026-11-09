import { Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { ErrorState, LoadingState } from '@/shared/ui';
import { learnedQuery, type LearnedAct } from '../api/learnedApi';
import { LearnedActDialog } from './LearnedActDialog';
import { LearnedChangeCard } from './LearnedChangeCard';

/**
 * What learning has changed or proposes to change, among the few settings it may touch. It only ever tightens by itself;
 * anything less cautious waits for the owner's password. Nothing here runs on load: every act is a confirmed click.
 */
export default function LearnedChangesPanel() {
  const learned = useQuery(learnedQuery);
  const [open, setOpen] = useState<{ id: number; act: LearnedAct } | null>(null);
  if (learned.isPending) return <LoadingState label="Loading learned changes" />;
  if (learned.isError) return <ErrorState error={learned.error} />;

  const { data } = learned;
  const target = open === null ? undefined : data.items.find((c) => c.id === open.id);
  return (
    <Stack component="section" aria-label="Learned changes" spacing={1.5}>
      <Typography variant="body2">
        Automatic changes are {data.autoOn ? 'ON' : 'OFF'}. {data.autoAppliedThisWeek} of {data.weeklyCap} automatic
        changes used in the last 7 days. An automatic change is recorded as auto, never as your approval. Changes take
        effect {data.takesEffect}.
      </Typography>
      <Typography variant="body2" color="text.secondary">
        Learning may touch only: {data.allowlist.map((s) => s.label).join(', ') || 'nothing yet'}. A change that loosens
        a safety, or undoing a tightening, asks for your password.
      </Typography>
      {data.items.length === 0 ? (
        <Typography>No learned changes yet. Learning has not proposed or applied anything.</Typography>
      ) : (
        <div>
          {data.items.map((change) => (
            <LearnedChangeCard
              key={change.id}
              change={change}
              onAct={(act) => {
                setOpen({ id: change.id, act });
              }}
            />
          ))}
        </div>
      )}
      {open !== null && target !== undefined && (
        <LearnedActDialog
          key={`${String(open.id)}-${open.act}`}
          change={target}
          act={open.act}
          onClose={() => {
            setOpen(null);
          }}
        />
      )}
    </Stack>
  );
}
