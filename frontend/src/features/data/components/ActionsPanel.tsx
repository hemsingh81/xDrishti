import AddIcon from '@mui/icons-material/Add';
import UploadIcon from '@mui/icons-material/UploadFileOutlined';
import { Box, Button, Card, Divider } from '@mui/material';
import { useState } from 'react';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { actionsList } from '../api/dataQueries';
import type { ActionsViewState } from '../hooks/useActionsView';
import { actionsViewLabels, type ActionsView } from '../lib/actions';
import { ActionDialog } from './ActionDialog';
import { ActionsList } from './ActionsList';
import { CsvEndNotice } from './CsvEndNotice';
import { DetectActionsButton } from './DetectActionsButton';
import { FilterChips } from './FilterChips';
import { ImportActionsDialog } from './ImportActionsDialog';

/** The chips in the order the owner works through them. */
const views: readonly ActionsView[] = ['suggested', 'applied', 'ignored', 'all'];

/**
 * Corporate actions (splits and bonus issues, P1-10a): the list with a status filter, Confirm / Edit / Ignore on the
 * suggestions, "Add action", "Import CSV" and "Detect now". Bars stay raw; the applied actions adjust prices when a read
 * asks for it. The list loads further rows as it is scrolled; the filter is in the URL.
 */
export function ActionsPanel({ state, pending }: { state: ActionsViewState; pending: number }) {
  const { view, update } = state;
  const result = useInfiniteRows(actionsList(view));
  const [dialog, setDialog] = useState<'add' | 'import' | null>(null);
  // The suggestions waiting (whatever the filter) and the size of the list that is shown; the API gives no other counts.
  const suggested = result.lastPage?.pending ?? pending;
  const shown = result.isPending || result.isPlaceholderData ? undefined : result.total;
  const count = (value: ActionsView) => (value === 'suggested' ? suggested : value === view ? shown : undefined);

  return (
    <Card sx={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: 0 }}>
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1.25, alignItems: 'center', px: 1.5, py: 1 }}>
        <FilterChips
          label="Corporate action status"
          value={view}
          options={views.map((value) => ({ value, label: actionsViewLabels[value], count: count(value) }))}
          onChange={(next) => {
            update(next);
          }}
        />
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', ml: { sm: 'auto' } }}>
          <Button
            size="small"
            variant="outlined"
            startIcon={<AddIcon />}
            onClick={() => {
              setDialog('add');
            }}
          >
            Add action
          </Button>
          <Button
            size="small"
            variant="outlined"
            startIcon={<UploadIcon />}
            onClick={() => {
              setDialog('import');
            }}
          >
            Import CSV
          </Button>
          <DetectActionsButton />
        </Box>
      </Box>
      <CsvEndNotice />
      <Divider />
      <ActionsList result={result} view={view} />
      {dialog === 'add' && (
        <ActionDialog
          onClose={() => {
            setDialog(null);
          }}
        />
      )}
      {dialog === 'import' && (
        <ImportActionsDialog
          onClose={() => {
            setDialog(null);
          }}
        />
      )}
    </Card>
  );
}
