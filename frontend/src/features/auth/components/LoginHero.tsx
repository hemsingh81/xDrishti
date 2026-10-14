import DnsIcon from '@mui/icons-material/DnsOutlined';
import FactCheckIcon from '@mui/icons-material/ChecklistOutlined';
import PieChartIcon from '@mui/icons-material/PieChartOutlined';
import PsychologyIcon from '@mui/icons-material/PsychologyOutlined';
import { Box, Typography } from '@mui/material';
import { alpha, keyframes } from '@mui/material/styles';
import { visuallyHidden } from '@mui/utils';
import type { SvgIconComponent } from '@mui/icons-material';
import { BrandMark, IconChip } from '@/shared/ui';
import { palettes, rem, shape, type ColorMode, type IconHueName } from '@/theme';

const rise = keyframes`from { opacity: 0; transform: translateY(12px); }`;

const features: readonly { title: string; text: string; icon: SvgIconComponent; hue: IconHueName }[] = [
  {
    title: 'Learns from every trade',
    text: 'Graded on your real fills; setups, exits and probabilities improve',
    icon: PsychologyIcon,
    hue: 'learning',
  },
  {
    title: 'Next-day plan',
    text: 'Conditional tickets you approve — or auto-approve within limits',
    icon: FactCheckIcon,
    hue: 'review',
  },
  { title: 'Portfolio & XIRR', text: 'All Dhan accounts, grouped by basket', icon: PieChartIcon, hue: 'portfolio' },
  {
    title: 'Runs as services',
    text: 'Local on Podman — keeps working while you are signed out',
    icon: DnsIcon,
    hue: 'services',
  },
];

function heroBackground(mode: ColorMode): string {
  const p = palettes[mode];
  return mode === 'dark'
    ? `linear-gradient(160deg, ${p.accentSoft} 0%, ${p.surfaceAlt} 70%)`
    : `linear-gradient(160deg, ${p.accentSoft} 0%, ${p.background} 60%, ${p.infoSoft} 100%)`;
}

/**
 * The left panel of the sign-in page (the prototype's `.login-hero`): brand, headline, a short description and what the
 * app does. It carries no live status — nothing about the system is shown before sign-in. Below 860 px only the brand row
 * stays (the form comes first, this panel is a footer band); the headline stays as a visually hidden <h1>.
 */
export function LoginHero() {
  return (
    <Box
      component="section"
      aria-label="About xDrishti"
      sx={(theme) => ({
        display: 'flex',
        flexDirection: 'column',
        gap: '14px',
        padding: '44px 44px 28px',
        borderRight: `1px solid ${theme.palette.divider}`,
        // The brand tints from the tokens: accent soft into the page background (and the info tint in light mode).
        background: heroBackground(theme.palette.mode),
        '@media (min-width: 1800px)': { padding: '52px 52px 32px' },
        [theme.breakpoints.down(861)]: {
          borderRight: 0,
          borderTop: `1px solid ${theme.palette.divider}`,
          padding: '20px 24px',
          gap: '8px',
          '& .hero-more': { display: 'none' },
          // The page's <h1> stays for assistive technology at every width; only its look goes.
          '& .hero-headline': visuallyHidden,
        },
      })}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
        <BrandMark size={42} />
        <Box>
          <Typography sx={{ fontWeight: 700, fontSize: rem(18), lineHeight: 1.5 }}>xDrishti</Typography>
          <Typography
            lang="hi"
            sx={{ fontSize: rem(12), fontWeight: 500, color: 'text.secondary', letterSpacing: '0.02em' }}
          >
            दृष्टि · vision, insight
          </Typography>
        </Box>
      </Box>
      <Typography
        component="h1"
        className="hero-headline"
        sx={{
          mt: '14px',
          fontSize: 'clamp(26px, 2.6vw, 34px)',
          fontWeight: 750,
          lineHeight: 1.15,
          letterSpacing: '-0.02em',
        }}
      >
        See your edge clearly. Trade it with discipline.
      </Typography>
      <Typography className="hero-more" sx={{ color: 'text.secondary', fontSize: rem(15), maxWidth: 420 }}>
        Your personal, self-learning NSE trading desk — plans tomorrow, learns from what actually happened, and keeps
        you in control.
      </Typography>
      <Box
        component="ul"
        className="hero-more"
        sx={{ listStyle: 'none', p: 0, mt: '10px', mb: 0, display: 'grid', gap: '10px' }}
      >
        {features.map(({ title, text, icon, hue }, index) => (
          <Box
            component="li"
            key={title}
            sx={(theme) => ({
              display: 'flex',
              gap: '12px',
              alignItems: 'center',
              padding: '10px 12px',
              borderRadius: `${shape.radius}px`,
              backgroundColor: alpha(theme.palette.background.paper, 0.7),
              border: `1px solid ${alpha(theme.palette.divider, 0.7)}`,
              fontSize: rem(13),
              color: 'text.secondary',
              animation: `${rise} 0.5s cubic-bezier(0.2, 0.8, 0.2, 1) both`,
              animationDelay: `${0.1 + index * 0.06}s`,
            })}
          >
            <IconChip icon={icon} hue={hue} size="sm" />
            <Box>
              <Box component="b" sx={{ display: 'block', color: 'text.primary', fontSize: rem(13.5) }}>
                {title}
              </Box>
              {text}
            </Box>
          </Box>
        ))}
      </Box>
    </Box>
  );
}
