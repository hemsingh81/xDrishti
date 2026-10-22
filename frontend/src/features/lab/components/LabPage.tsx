import ScienceIcon from '@mui/icons-material/ScienceOutlined';
import { Grid } from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { PageHeader } from '@/shared/ui';
import { useToast } from '@/shared/ui/toastContext';
import { versionQuery } from '../api/labQueries';
import { EditorCard, type Draft } from './EditorCard';
import { LibraryCard } from './LibraryCard';

interface Origin {
  id: string;
  version: number;
}

/** Strategy Lab: the library and the YAML editor (runs and reports arrive with P5-08b). */
export function LabPage() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [familyId, setFamilyId] = useState<string | null>(null);
  const [origin, setOrigin] = useState<Origin | null>(null);
  const [draft, setDraft] = useState<Draft>({ text: '', parentHash: null });

  const open = async (id: string, version: number) => {
    try {
      const v = await queryClient.query(versionQuery(id, version));
      setFamilyId(id);
      setOrigin({ id, version });
      setDraft({ text: v.text, parentHash: null });
    } catch (e) {
      toast.notify(e instanceof Error ? e.message : 'The version could not be read.', 'error');
    }
  };

  return (
    <>
      <PageHeader
        title="Strategy Lab"
        icon={ScienceIcon}
        hue="lab"
        description="Write rules, see the problems as you type and keep every version. Closed bars only."
      />
      <Grid container spacing={2}>
        <Grid size={{ xs: 12, lg: 4 }}>
          <LibraryCard
            familyId={familyId}
            version={origin?.version ?? null}
            onSelectFamily={setFamilyId}
            onOpenVersion={(id, v) => void open(id, v)}
          />
        </Grid>
        <Grid size={{ xs: 12, lg: 8 }}>
          <EditorCard
            draft={draft}
            origin={origin}
            onChange={setDraft}
            onReplace={(next) => {
              setDraft(next);
              setOrigin(null);
            }}
            onSaved={(c) => void open(c.id, c.version)}
          />
        </Grid>
      </Grid>
    </>
  );
}
