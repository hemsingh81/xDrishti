import { Alert, Button } from '@mui/material';
import { reportKindText, statementText, withoutSidesText, type DetectedKind } from '../lib/wrongFile';

interface WrongFileNoticeProps {
  /** What the API found in the file. */
  detected: DetectedKind;
  /** The way on for a file that belongs to the other import: opens it. Absent: the notice only explains. */
  onSwitch?: () => void;
  /** The way on for one of Dhan's downloaded reports: opens Import files. */
  onImportFiles?: () => void;
}

/**
 * A file that went to the wrong import (400 `portfolio.wrong_file_kind`): it says what the file is, with a button to the import
 * it belongs to; a tradebook without a side column, or a statement of amounts, is explained (neither import can read it), with no redirect.
 * One of Dhan's downloaded reports (transaction report, DP charges, demat holding, profit and loss) leads to Import files.
 */
export function WrongFileNotice({ detected, onSwitch, onImportFiles }: WrongFileNoticeProps) {
  const report = reportKindText[detected];
  if (report !== undefined) {
    return (
      <Alert
        severity="info"
        action={
          onImportFiles ? (
            <Button color="inherit" size="small" onClick={onImportFiles}>
              Import files
            </Button>
          ) : undefined
        }
      >
        {`${report} Nothing was imported here.`}
      </Alert>
    );
  }
  if (detected === 'tradebook_without_sides') return <Alert severity="warning">{withoutSidesText}</Alert>;
  if (detected === 'statement') return <Alert severity="warning">{statementText}</Alert>;
  const dividends = detected === 'dividends';
  return (
    <Alert
      severity="info"
      action={
        onSwitch ? (
          <Button color="inherit" size="small" onClick={onSwitch}>
            {dividends ? 'Import dividends' : 'Import trades'}
          </Button>
        ) : undefined
      }
    >
      {dividends
        ? 'This is a dividend file, not a tradebook. Nothing was imported.'
        : 'This is a tradebook, not a dividend list. Nothing was imported.'}
    </Alert>
  );
}
