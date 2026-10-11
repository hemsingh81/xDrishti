import { Box } from '@mui/material';
import type { SvgIconComponent } from '@mui/icons-material';
import { iconChip, useIconHue, type IconHueName } from '@/theme';

export interface IconChipProps {
  icon: SvgIconComponent;
  hue: IconHueName;
  size?: 'sm' | 'md' | 'lg';
  /** Assistive text; set only when the chip stands alone. Otherwise the chip is decorative (hidden from assistive tech). */
  label?: string;
}

/** Rounded-square icon chip tinted from a hue token (prototype `.nav-ic`, `.h-ico`, `.t-ico`). */
export function IconChip({ icon: Icon, hue, size = 'sm', label }: IconChipProps) {
  const { color, background } = useIconHue(hue);
  const dims = iconChip[size];
  return (
    <Box
      component="span"
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      data-hue={hue}
      sx={{
        width: dims.box,
        height: dims.box,
        flex: 'none',
        borderRadius: `${dims.radius}px`,
        display: 'inline-grid',
        placeItems: 'center',
        color,
        bgcolor: background,
      }}
    >
      <Icon sx={{ fontSize: dims.icon }} />
    </Box>
  );
}
