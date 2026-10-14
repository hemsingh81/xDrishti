import { useToast } from '@/shared/ui/toastContext';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import type { DividendView } from '@/shared/api';
import { useVoidDividend } from '../../api/dividendMutations';
import { problemText } from '../../lib/errors';
import { dividendName } from '../../lib/income';

interface VoidDividendDialogProps {
  dividend: DividendView;
  /** The dividend was voided: the caller puts the keyboard somewhere that is still on the page (the Void button is gone). */
  onVoided: () => void;
  onClose: () => void;
}

/** "Void this dividend?": it stops counting in the returns; it is never deleted, it stays in the list, marked voided. */
export function VoidDividendDialog({ dividend, onVoided, onClose }: VoidDividendDialogProps) {
  const { notify } = useToast();
  const voidDividend = useVoidDividend();
  return (
    <ConfirmDialog
      open
      title="Void this dividend?"
      confirmLabel="Void dividend"
      destructive
      busy={voidDividend.isPending}
      onCancel={onClose}
      onConfirm={() => {
        voidDividend.mutate(dividend.id, {
          onSuccess: () => {
            notify(`Voided the ${dividendName(dividend)}. It no longer counts in the returns.`);
            onClose();
            onVoided();
          },
          onError: (error) => {
            notify(problemText(error, `Could not void the ${dividendName(dividend)}. Nothing was changed.`), 'error');
            onClose();
          },
        });
      }}
    >
      {`The ${dividendName(dividend)}${dividend.symbol ? ` (${dividend.symbol})` : ''} stops counting in total return and net XIRR. It is not deleted: it stays in the list, marked voided.`}
    </ConfirmDialog>
  );
}
