import { Box, Typography } from '@mui/material';
import { visuallyHidden } from '@mui/utils';

/** The owner's question: text only, on the right. */
export function UserMessage({ text }: { text: string }) {
  return (
    <Box
      sx={{
        alignSelf: 'flex-end',
        maxWidth: '85%',
        bgcolor: 'primary.main',
        color: 'primary.contrastText',
        borderRadius: 2,
        px: 1.5,
        py: 1,
        overflowWrap: 'anywhere',
      }}
    >
      <Box component="span" sx={visuallyHidden}>
        You asked:{' '}
      </Box>
      <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap', color: 'inherit' }}>
        {text}
      </Typography>
    </Box>
  );
}
