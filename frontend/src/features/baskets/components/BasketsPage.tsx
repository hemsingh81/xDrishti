import AddIcon from '@mui/icons-material/Add';
import ShoppingBasketIcon from '@mui/icons-material/ShoppingBasketOutlined';
import { Alert, Box, Button, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import type { Basket } from '@/shared/api';
import { LoadingState, PageHeader } from '@/shared/ui';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { ToastProvider } from '@/shared/ui/ToastProvider';
import { useToast } from '@/shared/ui/toastContext';
import { useArchiveBasket } from '../api/basketMutations';
import { basketsQuery } from '../api/basketsQueries';
import { useBasketParams } from '../hooks/useBasketParams';
import { basketProblemText } from '../lib/errors';
import { BasketDialog } from './BasketDialog';
import { BasketsWorkspace } from './BasketsWorkspace';

type Editor = { kind: 'create' } | { kind: 'edit'; basket: Basket } | null;

/**
 * The Baskets screen. From `md` up it fills the viewport under the top bar (the cards and the members grid scroll
 * inside themselves); below `md` the page scrolls. The selected basket and the look-back date live in the URL.
 */
export function BasketsPage() {
  return (
    <ToastProvider>
      <BasketsContent />
    </ToastProvider>
  );
}

function BasketsContent() {
  const { notify } = useToast();
  const list = useQuery(basketsQuery);
  const { basketId, asOf, select, setAsOf } = useBasketParams();
  const archive = useArchiveBasket();
  const [editor, setEditor] = useState<Editor>(null);
  const [archiving, setArchiving] = useState<Basket | null>(null);

  const baskets = list.data ?? [];
  // Without a (valid) choice the first of the owner's baskets is shown; portfolio baskets only when there is none.
  const selected = baskets.find((b) => b.id === basketId) ?? baskets.find((b) => !b.system) ?? baskets[0];

  const confirmArchive = () => {
    if (!archiving) return;
    archive.mutate(archiving.id, {
      onSuccess: () => {
        notify(`${archiving.name} archived. Its history is kept.`);
        select(null, { replace: true });
        setArchiving(null);
      },
      onError: (error) => {
        notify(basketProblemText(error, 'The basket could not be archived.'), 'error');
        setArchiving(null);
      },
    });
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: 0, height: { md: '100%' } }}>
      <PageHeader
        dense
        title="Baskets"
        icon={ShoppingBasketIcon}
        hue="baskets"
        description="Group instruments in baskets. Drag rows onto a basket to copy them (hold Alt or Option to move) or use the Copy to and Move to menus."
        actions={
          <Button
            variant="contained"
            startIcon={<AddIcon />}
            onClick={() => {
              setEditor({ kind: 'create' });
            }}
          >
            New basket
          </Button>
        }
      />
      {list.isPending && <LoadingState label="Loading baskets" />}
      {list.isError && !list.data && (
        <Alert
          severity="error"
          action={
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                void list.refetch();
              }}
            >
              Retry
            </Button>
          }
        >
          {basketProblemText(list.error, 'The baskets could not be loaded.')}
        </Alert>
      )}
      {list.data && !selected && (
        <Box sx={{ p: 4, textAlign: 'center' }}>
          <Typography variant="h3" component="p" sx={{ mb: 1 }}>
            No baskets yet
          </Typography>
          <Typography color="text.secondary">Create a basket to start grouping instruments.</Typography>
        </Box>
      )}
      {selected && (
        <BasketsWorkspace
          baskets={baskets}
          selected={selected}
          onSelect={select}
          asOf={asOf}
          onAsOfChange={setAsOf}
          onEdit={() => {
            setEditor({ kind: 'edit', basket: selected });
          }}
          onArchive={() => {
            setArchiving(selected);
          }}
        />
      )}
      {editor && (
        <BasketDialog
          basket={editor.kind === 'edit' ? editor.basket : undefined}
          onClose={() => {
            setEditor(null);
          }}
          onSaved={(saved) => {
            if (editor.kind === 'create') select(saved.id);
          }}
        />
      )}
      <ConfirmDialog
        open={archiving !== null}
        title={`Archive ${archiving?.name ?? 'basket'}?`}
        confirmLabel="Archive"
        destructive
        busy={archive.isPending}
        onCancel={() => {
          setArchiving(null);
        }}
        onConfirm={confirmArchive}
      >
        The basket disappears from this list and from scan scopes. Its members and history are kept, and the instruments
        stay tracked.
      </ConfirmDialog>
    </Box>
  );
}
