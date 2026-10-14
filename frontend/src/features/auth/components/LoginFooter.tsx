import DarkModeIcon from '@mui/icons-material/DarkModeOutlined';
import LightModeIcon from '@mui/icons-material/LightModeOutlined';
import { Box, Button, Typography } from '@mui/material';
import { rem, useColorMode } from '@/theme';

/** "Personal use · single owner" and the colour-mode toggle, under the form (the prototype's `.login-foot`). */
export function LoginFooter() {
  const { mode, toggle } = useColorMode();
  return (
    <Box
      component="footer"
      sx={{
        mt: '28px',
        mx: 'auto',
        width: '100%',
        maxWidth: 380,
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        flexWrap: 'wrap',
        gap: 1,
      }}
    >
      <Typography sx={{ fontSize: rem(11.5), color: 'text.tertiary' }}>Personal use · single owner</Typography>
      <Button
        size="small"
        color="inherit"
        variant="text"
        startIcon={mode === 'dark' ? <LightModeIcon /> : <DarkModeIcon />}
        onClick={toggle}
      >
        {mode === 'dark' ? 'Light mode' : 'Dark mode'}
      </Button>
    </Box>
  );
}
