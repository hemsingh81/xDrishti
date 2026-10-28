import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Card, CardContent, CardHeader, Chip, Stack, Typography } from '@mui/material';
import { formatDate } from '@/shared/lib/format';
import type { ComplianceRecord } from '../api/complianceQueries';
import { kindLabels, kinds, outcomeLabel, stateLabel } from '../lib/labels';

/** "Overdue" with an icon and the word, never colour alone. */
export function OverdueChip() {
  return <Chip size="small" color="warning" variant="outlined" icon={<WarningIcon />} label="Overdue" />;
}

function KindCard({ kind, record }: { kind: string; record: ComplianceRecord | null }) {
  const title = kindLabels[kind] ?? kind;
  return (
    <Card variant="outlined" component="section" aria-label={title} sx={{ flex: '1 1 260px' }}>
      <CardHeader
        title={title}
        slotProps={{ title: { variant: 'subtitle1' } }}
        action={record?.overdue ? <OverdueChip /> : null}
      />
      <CardContent>
        {record === null ? (
          <Typography color="text.secondary">Nothing recorded yet. Start with the runbook, section 3.</Typography>
        ) : (
          <Stack sx={{ gap: 0.5 }}>
            <Typography sx={{ fontWeight: 600 }}>{outcomeLabel(record.outcome)}</Typography>
            <Typography variant="body2">Decided {formatDate(record.decidedOn)}</Typography>
            <Typography variant="body2">
              Next review {formatDate(record.nextReviewOn)}
              {record.overdue ? ' (overdue)' : ''}
            </Typography>
            {record.staticIpState && <Typography variant="body2">State: {stateLabel(record.staticIpState)}</Typography>}
            {record.lockEndsOn && (
              <Typography variant="body2">Dhan&apos;s lock on the IP ends {formatDate(record.lockEndsOn)}</Typography>
            )}
          </Stack>
        )}
      </CardContent>
    </Card>
  );
}

/** The newest record of each kind: what the P11 phase check reads. A kind with no record is empty, which is not "no change". */
export function ComplianceCards({ latest }: { latest: Record<string, ComplianceRecord | null> }) {
  return (
    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 2 }}>
      {kinds.map((kind) => (
        <KindCard key={kind} kind={kind} record={latest[kind] ?? null} />
      ))}
    </Box>
  );
}
