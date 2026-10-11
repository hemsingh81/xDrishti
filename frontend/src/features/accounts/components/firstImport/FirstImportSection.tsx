import { Alert, Stack, TextField, Typography } from '@mui/material';
import { minAcquiredOn, todayIso } from '@/shared/lib/dates';
import type { FirstImport } from '../../hooks/useFirstImport';
import { FirstImportList } from './FirstImportList';

interface FirstImportSectionProps {
  idPrefix: string;
  /** yyyy-MM-dd */
  date: string;
  dateError?: string;
  onDateChange: (date: string) => void;
  /** The account has holdings already: the first import happened, so only a note is shown. */
  hasHoldings: boolean;
  /** The loaded first import; absent when there is no account yet (the wizard). */
  firstImport?: FirstImport;
  serverErrors?: Readonly<Record<string, string>>;
  disabled?: boolean;
  /** The owner changed a date in the list. */
  onEdit?: () => void;
}

/**
 * The first import: the date holdings get when they have none of their own and, for a connected account without
 * holdings, a date per holding. Once holdings exist this is only a note.
 */
export function FirstImportSection({
  idPrefix,
  date,
  dateError,
  onDateChange,
  hasHoldings,
  firstImport,
  serverErrors = {},
  disabled = false,
  onEdit,
}: FirstImportSectionProps) {
  return (
    <Stack component="section" aria-label="First import" spacing={1.5}>
      <Typography variant="h3">First import</Typography>
      {hasHoldings ? (
        <Alert role="status" severity="info">
          Holdings are refreshed automatically; new ones are dated the day they appear.
        </Alert>
      ) : (
        <>
          <TextField
            type="date"
            size="small"
            id={`${idPrefix}-first-import-date`}
            label="First import date"
            value={date}
            disabled={disabled}
            error={dateError !== undefined}
            helperText={
              dateError ??
              'All listed holdings are imported; those without a date of their own get this date. Holdings that appear later are dated the day they appear. If Daily refresh is turned on and no holdings are imported yet, the first refresh imports every holding dated with the first import date.'
            }
            onChange={(event) => {
              onDateChange(event.target.value);
            }}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: minAcquiredOn, max: todayIso() } }}
            sx={{ maxWidth: 360 }}
          />
          {firstImport?.failed && (
            <Alert role="status" severity="warning">
              Holdings could not be loaded. If you save now, the first sync imports all holdings with the first import
              date ({date || 'not set'}).
            </Alert>
          )}
          {firstImport?.eligible ? (
            <FirstImportList
              firstImport={firstImport}
              defaultDate={date}
              serverErrors={serverErrors}
              disabled={disabled}
              onEdit={onEdit ?? (() => undefined)}
            />
          ) : (
            <Alert role="status" severity="info">
              {!firstImport
                ? 'Once the account is connected, the first sync imports every holding with this date.'
                : firstImport.connected
                  ? 'Turn the Portfolio role on to import holdings. The first sync then imports every holding with this date.'
                  : 'The account is not connected to Dhan, so there is no list to choose dates from. The first sync imports every holding with this date.'}
            </Alert>
          )}
        </>
      )}
    </Stack>
  );
}
