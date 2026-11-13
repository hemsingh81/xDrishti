import LockOpenIcon from '@mui/icons-material/LockOpenOutlined';
import { Alert, Box, Button, Chip, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { formatDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { blockedByWords, holdOutQuery, lookWords, type HoldOutBlock } from '../api/holdOutQueries';
import { ReleaseDialog } from './ReleaseDialog';

const stateWord: Record<string, string> = {
  filling: 'Filling',
  sealed: 'Sealed, no look',
  looked: 'Looked',
  released: 'Released',
};

function LooksLine({ block }: { block: HoldOutBlock }) {
  const looks = block.looks;
  if (!looks) return null;
  const rows: [string, (typeof looks)['strategy']][] = [
    ['Strategy', looks.strategy],
    ['Model', looks.model],
    ['System', looks.system],
  ];
  return (
    <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mt: 0.5 }}>
      {rows.map(([name, look]) => (
        <Chip
          key={name}
          size="small"
          variant="outlined"
          color={look.state === 'fresh' ? 'success' : 'default'}
          label={`${name}: ${lookWords[look.state] ?? look.state}${look.state === 'fresh' && look.status ? `, ${look.status}` : ''}`}
        />
      ))}
    </Stack>
  );
}

/**
 * The chain of hold-out blocks (HO-01): each block with its state, its three looks (one fresh look per kind) and, for the block in force, what
 * keeps the automatic release from acting. The owner's release sits behind a password dialog that states the forfeit for a block with no look.
 */
export function HoldOutBlocksCard() {
  const query = useQuery(holdOutQuery());
  const [releasing, setReleasing] = useState<HoldOutBlock | null>(null);
  if (query.isPending) return <LoadingState announce={false} label="Reading the hold-out blocks" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const blocks = query.data.blocks ?? [];
  const inForce = blocks.find((b) => b.state !== 'released');
  return (
    <Box component="section" aria-label="Hold-out blocks">
      <Typography variant="subtitle2">Hold-out blocks</Typography>
      {blocks.length === 0 ? (
        <Typography variant="body2">No block is fixed yet. The first one is fixed at the first Lab run.</Typography>
      ) : (
        <Stack spacing={1.5} sx={{ mt: 1 }}>
          {blocks.map((block) => (
            <Box key={block.start}>
              <Typography component="span" variant="body2" sx={{ mr: 1 }}>
                {`${formatDate(block.start)} to ${formatDate(block.historyLast)}`}
              </Typography>
              <Chip size="small" label={stateWord[block.state ?? ''] ?? block.state} />
              {block.state === 'released' && block.releasedAt && (
                <Typography component="span" variant="body2" sx={{ ml: 1 }}>
                  {`released ${formatDate(block.releasedAt)} (${block.mode ?? 'owner'})`}
                </Typography>
              )}
              <LooksLine block={block} />
              {block === inForce && block.blockedBy && (
                <Alert severity="info" sx={{ my: 1 }}>
                  {blockedByWords[block.blockedBy] ?? block.blockedBy}
                </Alert>
              )}
              {block === inForce && (block.state === 'sealed' || block.state === 'looked') && (
                <Button
                  startIcon={<LockOpenIcon />}
                  onClick={() => {
                    setReleasing(block);
                  }}
                >
                  Release this block
                </Button>
              )}
            </Box>
          ))}
        </Stack>
      )}
      {releasing && (
        <ReleaseDialog
          block={releasing}
          onClose={() => {
            setReleasing(null);
          }}
        />
      )}
    </Box>
  );
}
