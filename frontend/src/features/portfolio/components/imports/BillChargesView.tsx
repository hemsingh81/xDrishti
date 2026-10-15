import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Stack, Typography } from '@mui/material';
import type { BillCharges } from '@/shared/api';
import { formatInrPrecise } from '@/shared/lib/format';

/** The charges of the report's rows added up against the report's own total: they should agree to the paisa. */
export function BillChargesView({ charges }: { charges: BillCharges }) {
  const Icon = charges.matches ? CheckIcon : WarningIcon;
  return (
    <Box>
      <Typography component="h5" variant="h4" sx={{ mb: 0.5 }}>
        Charges
      </Typography>
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1 }}>
        <Icon fontSize="small" color={charges.matches ? 'success' : 'warning'} aria-hidden />
        <Typography variant="body2">
          {charges.reportTotal === null
            ? `The rows add up to ${formatInrPrecise(charges.total)} of charges; the report states no total to compare with.`
            : charges.matches
              ? `The charges add up to ${formatInrPrecise(charges.total)}, which matches the report’s own total (${formatInrPrecise(charges.reportTotal)}).`
              : `The charges add up to ${formatInrPrecise(charges.total)}, which does not match the report’s own total (${formatInrPrecise(charges.reportTotal)}).`}
        </Typography>
      </Stack>
    </Box>
  );
}
