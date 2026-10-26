import { Link } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { useCapability } from '@/features/owner-acts';

interface MoveLinkProps {
  /** The owner-act switch that allows the move. */
  capability: 'learning_moves_ui' | 'autonomy_form';
  /** The form to open when the switch is on. */
  to: string;
  label: string;
}

/**
 * A plain link to an owner-act form (P8-19). Learning stays read only: with the switch ON it links to the form, which asks for a password and a reason; with
 * it OFF (the default) it says so and links to the switches. Nothing here writes.
 */
export function MoveLink({ capability, to, label }: MoveLinkProps) {
  const { enabled, loaded } = useCapability(capability);
  if (!loaded) return null;
  return enabled ? (
    <Link component={RouterLink} to={to}>
      {label}
    </Link>
  ) : (
    <Link component={RouterLink} to="/owner-acts">
      Moves from the screen are switched off: owner-act switches
    </Link>
  );
}
