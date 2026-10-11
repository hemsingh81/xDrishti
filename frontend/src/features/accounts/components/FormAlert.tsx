import { Alert } from '@mui/material';

/** Summary of a failed save that is not about one field (conflict, not found, outage…). Renders nothing without text. */
export function FormAlert({ message }: { message: string | undefined }) {
  return message ? <Alert severity="error">{message}</Alert> : null;
}
