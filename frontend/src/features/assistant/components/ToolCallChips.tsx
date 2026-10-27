import { Box, ButtonBase, Typography } from '@mui/material';
import { useId, useState } from 'react';
import { ToolCallDetail } from './ToolCallDetail';

export interface ChipCall {
  /** The stored id; null while the call is still running (nothing to open yet). */
  id: number | null;
  index: number;
  tool: string;
  rows: number | null;
  status: string | null;
}

const statusWord = (c: ChipCall) => c.status ?? 'running';

/** The tool calls under an answer, always visible; a button opens one (arguments, rows, evidence), loaded once. */
export function ToolCallChips({ calls }: { calls: readonly ChipCall[] }) {
  const [open, setOpen] = useState<number | null>(null);
  const base = useId();
  if (calls.length === 0) return null;
  const opened = calls.find((c) => c.index === open);
  return (
    <Box sx={{ mt: 1.5, minWidth: 0 }}>
      <Box
        component="ul"
        aria-label="Tool calls"
        sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, listStyle: 'none', m: 0, p: 0 }}
      >
        {calls.map((c) => {
          const rows = c.rows === null ? '' : ` · ${c.rows} ${c.rows === 1 ? 'row' : 'rows'}`;
          const label = `#${c.index} ${c.tool}${rows} · ${statusWord(c)}`;
          const expanded = open === c.index;
          return (
            <li key={c.index}>
              <ButtonBase
                disabled={c.id === null}
                aria-expanded={expanded}
                aria-controls={expanded ? `${base}-detail` : undefined}
                aria-label={`Tool call ${c.index}, ${c.tool}${c.rows === null ? '' : `, ${c.rows} ${c.rows === 1 ? 'row' : 'rows'}`}, ${statusWord(c)}`}
                onClick={() => {
                  setOpen(expanded ? null : c.index);
                }}
                sx={{
                  border: 1,
                  borderColor: c.status === 'ok' || c.status === null ? 'divider' : 'warning.main',
                  borderRadius: 1,
                  px: 1,
                  py: 0.5,
                  textAlign: 'left',
                }}
              >
                <Typography variant="caption" sx={{ fontFamily: 'monospace' }}>
                  {label}
                </Typography>
              </ButtonBase>
            </li>
          );
        })}
      </Box>
      <div id={`${base}-detail`}>
        {opened?.id !== null && opened?.id !== undefined && <ToolCallDetail id={opened.id} />}
      </div>
    </Box>
  );
}
