import { Step, StepLabel, Stepper, Typography, useMediaQuery, useTheme } from '@mui/material';
import { wizardSteps } from '../../lib/wizardState';

/** Step indicator: a stepper on wide screens, one line of text on phones (five labels do not fit in 360 px). */
export function WizardProgress({ step }: { step: number }) {
  const theme = useTheme();
  const wide = useMediaQuery(theme.breakpoints.up('md'));
  if (!wide) {
    return (
      <Typography component="p" sx={{ fontWeight: 600 }} aria-live="polite">
        Step {step + 1} of {wizardSteps.length}: {wizardSteps[step]}
      </Typography>
    );
  }
  return (
    <Stepper activeStep={step} alternativeLabel aria-label="Add account steps">
      {wizardSteps.map((label) => (
        <Step key={label}>
          <StepLabel>{label}</StepLabel>
        </Step>
      ))}
    </Stepper>
  );
}
