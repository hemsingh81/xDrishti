import SearchIcon from '@mui/icons-material/ManageSearchOutlined';
import { Button } from '@mui/material';
import { formatNumber } from '@/shared/lib/format';
import { useToast } from '@/shared/ui/toastContext';
import { useDetectActions } from '../api/actionMutations';
import { problemText } from '../lib/errors';

/** "Detect now": looks for splits and bonus issues in the stored day bars; it only suggests, nothing is applied. */
export function DetectActionsButton() {
  const { notify } = useToast();
  const detect = useDetectActions();

  const run = async () => {
    try {
      const { suggested, instruments, pending } = await detect.mutateAsync();
      const found = `${formatNumber(suggested)} ${suggested === 1 ? 'suggestion' : 'suggestions'} for review`;
      notify(
        suggested > 0
          ? `${found}. Checked ${formatNumber(instruments)} instruments.`
          : `No new suggestions. Checked ${formatNumber(instruments)} instruments; ${formatNumber(pending)} waiting for review.`,
        suggested > 0 ? 'success' : 'info',
      );
    } catch (error) {
      notify(problemText(error, 'The detection could not be run.'), 'error');
    }
  };

  return (
    <Button
      size="small"
      variant="outlined"
      startIcon={<SearchIcon />}
      loading={detect.isPending}
      onClick={() => {
        void run();
      }}
    >
      Detect now
    </Button>
  );
}
