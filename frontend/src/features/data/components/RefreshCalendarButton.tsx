import RefreshIcon from '@mui/icons-material/Refresh';
import { Button } from '@mui/material';
import { useToast } from '@/shared/ui/toastContext';
import { useRefreshCalendar } from '../api/calendarMutations';
import { problemText } from '../lib/errors';

/**
 * "Refresh from NSE": reads NSE's public holiday list. The answer is always a message: success says what changed,
 * a refusal (succeeded false) is a warning that also says the days can be added by hand.
 */
export function RefreshCalendarButton() {
  const { notify } = useToast();
  const refresh = useRefreshCalendar();

  const run = async () => {
    try {
      const result = await refresh.mutateAsync();
      if (result.succeeded) notify(result.message, 'success');
      else notify(`${result.message} You can add the holidays by hand.`, 'warning');
    } catch (error) {
      notify(`${problemText(error, 'The refresh failed.')} You can add the holidays by hand.`, 'error');
    }
  };

  return (
    <Button
      size="small"
      variant="outlined"
      startIcon={<RefreshIcon />}
      loading={refresh.isPending}
      onClick={() => {
        void run();
      }}
    >
      Refresh from NSE
    </Button>
  );
}
