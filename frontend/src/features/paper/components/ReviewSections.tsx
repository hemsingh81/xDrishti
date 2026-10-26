import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import type { ReviewSection } from '../api/reviewQueries';

function SectionTable({ title, table }: { title: string; table: NonNullable<ReviewSection['table']> }) {
  if (table.rows.length === 0) return null;
  return (
    <TableContainer role="region" aria-label={`${title}, scrolls sideways`} tabIndex={0}>
      <Table size="small" aria-label={title}>
        <TableHead>
          <TableRow>
            {table.columns.map((column) => (
              <TableCell key={column}>{column}</TableCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {table.rows.map((row, index) => (
            <TableRow key={`${row.join('|')}-${String(index)}`}>
              {row.map((cell, i) => (
                <TableCell key={`${table.columns[i] ?? ''}-${String(i)}`}>{cell}</TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}

/** The sections of a review as the server wrote them: lines, a table where there is one, and the limits sentence that ends each. */
export function ReviewSections({ sections }: { sections: readonly ReviewSection[] }) {
  return (
    <>
      {sections.map((section) => {
        // A section with a table shows its rows in the table, not twice.
        const lines = section.table ? section.lines.filter((line) => !line.startsWith('- ')) : section.lines;
        return (
          <Box component="section" key={section.id} aria-labelledby={`review-${section.id}`} sx={{ mb: 3 }}>
            <Typography variant="h2" id={`review-${section.id}`} sx={{ fontSize: '1.1rem', mb: 1 }}>
              {section.title}
            </Typography>
            <Box component="ul" sx={{ m: 0, pl: 2.5, overflowWrap: 'anywhere' }}>
              {lines.map((line, index) => (
                <li key={`${line}-${String(index)}`}>{line}</li>
              ))}
            </Box>
            {section.table && <SectionTable title={section.title} table={section.table} />}
            <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 1 }}>
              Limits: {section.limits}
            </Typography>
          </Box>
        );
      })}
    </>
  );
}
