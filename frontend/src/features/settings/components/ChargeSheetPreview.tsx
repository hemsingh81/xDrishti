import { Box, Typography } from '@mui/material';
import type { ChargeSheetStatusView } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';

/** The stored sheet as the owner is asked to confirm it: what it is, where its figures came from, and the hash of its rates. */
export function ChargeSheetPreview({ sheet }: { sheet: NonNullable<ChargeSheetStatusView['sheet']> }) {
  const rows: [string, string][] = [
    ['Sheet', `${sheet.id} - ${sheet.label}`],
    ['Source', sheet.source],
    ['Checked against', sheet.checked ?? 'Not recorded: this sheet is unchecked'],
    ['Schedules', `${sheet.schedules} from ${formatDate(sheet.firstEffective)} to ${formatDate(sheet.lastEffective)}`],
  ];
  return (
    <Box
      component="dl"
      sx={{ m: 0, display: 'grid', gridTemplateColumns: 'max-content 1fr', columnGap: 2, rowGap: 0.5 }}
    >
      {rows.map(([name, value]) => (
        <Box key={name} sx={{ display: 'contents' }}>
          <Typography component="dt" color="text.secondary">
            {name}
          </Typography>
          <Typography component="dd" sx={{ m: 0 }}>
            {value}
          </Typography>
        </Box>
      ))}
      <Typography component="dt" color="text.secondary">
        Rates SHA-256
      </Typography>
      <Typography component="dd" sx={{ m: 0, fontFamily: 'monospace', overflowWrap: 'anywhere' }}>
        {sheet.ratesSha256}
      </Typography>
    </Box>
  );
}
