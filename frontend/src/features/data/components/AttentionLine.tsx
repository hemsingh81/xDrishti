import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Link, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import type { DataOverview } from '@/shared/api';
import { attentionItems } from '../lib/attention';

/**
 * One line, only when something waits for the owner: "2 calendar proposals · 3 suggested corporate actions · 12 open
 * gaps", each a link to the tab that deals with it. Nothing is rendered when nothing waits.
 */
export function AttentionLine({ overview }: { overview: DataOverview }) {
  const items = attentionItems(overview);
  if (items.length === 0) return null;
  return (
    <Box
      component="section"
      aria-label="Needs attention"
      sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '2px 8px', mb: 1, flexShrink: 0 }}
    >
      <WarningIcon fontSize="small" color="warning" aria-hidden />
      <Typography variant="body2" sx={{ fontWeight: 600 }}>
        Needs your attention:
      </Typography>
      <Box component="ul" sx={{ display: 'flex', flexWrap: 'wrap', gap: '2px 8px', m: 0, p: 0, listStyle: 'none' }}>
        {items.map((item) => (
          <Box
            component="li"
            key={item.id}
            // The separator is drawn, not text: the links read as a list.
            sx={{ '&:not(:first-of-type)::before': { content: '"·"', mr: 1, color: 'text.secondary' } }}
          >
            <Link component={RouterLink} to={{ search: item.search }} variant="body2" sx={{ fontWeight: 600 }}>
              {item.text}
            </Link>
          </Box>
        ))}
      </Box>
    </Box>
  );
}
