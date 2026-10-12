import BuildIcon from '@mui/icons-material/BuildOutlined';
import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import RemoveIcon from '@mui/icons-material/RemoveCircleOutlineOutlined';
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Typography,
} from '@mui/material';
import type { DataOverview, QualityCounts } from '@/shared/api';
import { planFix } from '../lib/fixPlan';

interface FixIssuesDialogProps {
  open: boolean;
  /** The data overview is not known yet (nothing can be analysed). */
  data: Pick<DataOverview, 'dhan' | 'gaps' | 'aggregates'> | null;
  counts: Pick<QualityCounts, 'failed' | 'warned' | 'notChecked' | 'tracked'>;
  /** A job is running or was just started. */
  busy: boolean;
  onClose: () => void;
  /** Starts the fix; `dhan` = the Dhan step is part of it. */
  onRun: (dhan: boolean) => void;
}

/**
 * "Fix issues": analyses what is wrong (open gaps, aggregates behind, failing instruments), lists the steps a fix would
 * take — fetch from Dhan, rebuild the aggregates, check again — and runs them as one job on "Run fix".
 */
export function FixIssuesDialog({ open, data, counts, busy, onClose, onRun }: FixIssuesDialogProps) {
  const plan = data ? planFix(data, counts) : null;
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="fix-issues-title">
      <DialogTitle id="fix-issues-title">Fix data issues</DialogTitle>
      <DialogContent>
        {plan === null ? (
          <Typography color="text.secondary">Analysing the data…</Typography>
        ) : (
          <>
            {plan.needed ? (
              <Typography color="text.secondary" sx={{ mb: 1 }}>
                This is what was found and what Run fix will do, in this order:
              </Typography>
            ) : (
              <Alert severity="success" sx={{ mb: 1 }}>
                Nothing to fix: no missing bars, no aggregates waiting, every instrument passes.
              </Alert>
            )}
            <List dense aria-label="Fix steps">
              {plan.steps.map((step) => (
                <ListItem key={step.id} disableGutters sx={{ alignItems: 'flex-start' }}>
                  <ListItemIcon sx={{ minWidth: 36, mt: 0.5 }}>
                    {step.included ? <CheckIcon color="primary" /> : <RemoveIcon color="disabled" />}
                  </ListItemIcon>
                  <ListItemText
                    primary={step.title}
                    secondary={step.detail}
                    slotProps={{ primary: { sx: { fontWeight: 600 } } }}
                  />
                </ListItem>
              ))}
            </List>
            {busy && (
              <Typography variant="body2" color="text.secondary">
                A job is running; Run fix works again when it ends.
              </Typography>
            )}
          </>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Close</Button>
        <Button
          variant="contained"
          startIcon={<BuildIcon />}
          disabled={!plan?.needed}
          aria-disabled={busy || undefined}
          onClick={() => {
            if (plan && !busy) onRun(plan.dhan);
          }}
        >
          Run fix
        </Button>
      </DialogActions>
    </Dialog>
  );
}
