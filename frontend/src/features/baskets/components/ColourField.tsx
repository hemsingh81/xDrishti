import CheckIcon from '@mui/icons-material/Check';
import { Box, ButtonBase, FormHelperText, Stack, TextField, Typography } from '@mui/material';
import { basketColors, focusRing } from '@/theme';

interface ColourFieldProps {
  value: string;
  error?: string;
  onChange: (value: string) => void;
}

/** The app's palette as a set of radio swatches, plus a field for any `#rrggbb`. */
export function ColourField({ value, error, onChange }: ColourFieldProps) {
  const current = value.trim().toLowerCase();
  return (
    <Box>
      <Typography component="div" variant="body2" color="text.secondary" id="basket-colour-label" sx={{ mb: 0.75 }}>
        Colour
      </Typography>
      <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
        <Stack
          role="radiogroup"
          aria-labelledby="basket-colour-label"
          direction="row"
          sx={{ gap: 0.75, flexWrap: 'wrap' }}
        >
          {basketColors.map((colour) => {
            const checked = colour.value === current;
            return (
              <ButtonBase
                key={colour.value}
                role="radio"
                aria-checked={checked}
                aria-label={colour.name}
                title={colour.name}
                onClick={() => {
                  onChange(colour.value);
                }}
                style={{ backgroundColor: colour.value }}
                sx={(theme) => ({
                  width: 28,
                  height: 28,
                  borderRadius: '50%',
                  color: theme.palette.common.white,
                  '&:focus-visible': {
                    outline: `${focusRing.width}px solid ${theme.palette.primary.main}`,
                    outlineOffset: focusRing.offset,
                  },
                })}
              >
                {checked && <CheckIcon fontSize="small" />}
              </ButtonBase>
            );
          })}
        </Stack>
        <TextField
          size="small"
          label="Hex colour"
          value={value}
          error={Boolean(error)}
          onChange={(event) => {
            onChange(event.target.value);
          }}
          slotProps={{ htmlInput: { maxLength: 7, spellCheck: false, autoComplete: 'off' } }}
          sx={{ width: 130 }}
        />
      </Stack>
      {error && <FormHelperText error>{error}</FormHelperText>}
    </Box>
  );
}
