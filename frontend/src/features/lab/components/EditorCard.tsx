import DownloadIcon from '@mui/icons-material/DownloadOutlined';
import UploadIcon from '@mui/icons-material/UploadFileOutlined';
import { Alert, Button, Card, CardContent, CardHeader, Stack, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { lazy, Suspense, useMemo, useRef } from 'react';
import { LoadingState } from '@/shared/ui';
import { useToast } from '@/shared/ui/toastContext';
import {
  cloneVersion,
  createStrategy,
  downloadHref,
  labKeys,
  maxTextBytes,
  schemaQuery,
  validateQuery,
  type CreatedStrategy,
} from '../api/labQueries';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { toMarkers } from '../lib/markers';
import { ProblemList } from './ProblemList';

// Monaco is large: it loads when the editor first shows, not with the page.
const MonacoYamlEditor = lazy(() => import('./MonacoYamlEditor'));

export interface Draft {
  text: string;
  parentHash: string | null;
}

interface EditorCardProps {
  draft: Draft;
  /** The stored version the text came from, when it is shown unchanged from the library. */
  origin: { id: string; version: number } | null;
  /** Typing: the text changes, the stored version it came from stays. */
  onChange: (draft: Draft) => void;
  /** Clone or upload: a new draft that comes from no stored version. */
  onReplace: (draft: Draft) => void;
  onSaved: (created: CreatedStrategy) => void;
}

/** The editor with live validate, and Save, Clone, Upload and Download. */
export function EditorCard({ draft, origin, onChange, onReplace, onSaved }: EditorCardProps) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const fileInput = useRef<HTMLInputElement>(null);
  const debounced = useDebouncedValue(draft.text, 500);
  const schema = useQuery(schemaQuery);
  const validation = useQuery({ ...validateQuery(debounced), enabled: debounced.trim() !== '' });
  const markers = useMemo(() => toMarkers(validation.data?.problems ?? []), [validation.data]);
  const canSave = debounced === draft.text && validation.data?.valid === true && !validation.isFetching;

  const save = useMutation({
    mutationFn: () => createStrategy({ text: draft.text, parentHash: draft.parentHash }),
    onSuccess: (created) => {
      void queryClient.invalidateQueries({ queryKey: labKeys.strategies });
      void queryClient.invalidateQueries({ queryKey: ['lab', 'strategy', created.id] });
      toast.notify(
        created.created
          ? `Saved ${created.id} version ${created.version}.`
          : `Already stored as version ${created.version}.`,
        'success',
      );
      onSaved(created);
    },
    onError: (e) => {
      toast.notify(e.message, 'error');
    },
  });

  const clone = useMutation({
    mutationFn: () => {
      if (!origin) throw new Error('Open a stored version first.');
      return cloneVersion(origin.id, origin.version);
    },
    onSuccess: (c) => {
      onReplace({ text: c.text, parentHash: c.parentHash });
    },
    onError: (e) => {
      toast.notify(e.message, 'error');
    },
  });

  const upload = async (file: File | undefined) => {
    if (!file) return;
    if (file.size > maxTextBytes) {
      toast.notify(`That file is over ${maxTextBytes / 1024} KB, the largest the Lab takes.`, 'error');
      return;
    }
    onReplace({ text: await file.text(), parentHash: null });
  };

  return (
    <Card>
      <CardHeader
        title={<Typography variant="h2">Rules (YAML)</Typography>}
        action={
          <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
            <Button startIcon={<UploadIcon />} onClick={() => fileInput.current?.click()}>
              Upload
            </Button>
            <input
              ref={fileInput}
              type="file"
              accept=".yaml,.yml,text/yaml"
              aria-label="Upload a strategy file"
              hidden
              onChange={(e) => {
                void upload(e.target.files?.[0]);
                e.target.value = '';
              }}
            />
            {origin && (
              <>
                <Button
                  startIcon={<DownloadIcon />}
                  component="a"
                  href={downloadHref(origin.id, origin.version)}
                  download
                >
                  Download
                </Button>
                <Button
                  onClick={() => {
                    clone.mutate();
                  }}
                  disabled={clone.isPending}
                >
                  Clone
                </Button>
              </>
            )}
            <Button
              variant="contained"
              onClick={() => {
                save.mutate();
              }}
              disabled={!canSave || save.isPending}
            >
              Save
            </Button>
          </Stack>
        }
      />
      <CardContent>
        {schema.error && (
          <Alert severity="warning" sx={{ mb: 1 }}>
            The strategy schema could not be read; the editor works without completion hints.
          </Alert>
        )}
        <Suspense fallback={<LoadingState label="Loading the editor" />}>
          <MonacoYamlEditor
            value={draft.text}
            onChange={(text) => {
              onChange({ ...draft, text });
            }}
            schema={schema.data}
            markers={markers}
            label="Strategy text"
          />
        </Suspense>
        <div aria-live="polite">
          {draft.text.trim() === '' && (
            <Typography color="text.secondary" sx={{ mt: 1 }}>
              Open a version, clone one or upload a file. Running tests arrives with the next slice.
            </Typography>
          )}
          {validation.error && (
            <Alert severity="error" sx={{ mt: 1 }}>
              {validation.error.message}
            </Alert>
          )}
          {validation.data && draft.text.trim() !== '' && <ProblemList result={validation.data} />}
        </div>
      </CardContent>
    </Card>
  );
}
