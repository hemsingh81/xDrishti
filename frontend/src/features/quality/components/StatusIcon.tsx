import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import RemoveCircleIcon from '@mui/icons-material/RemoveCircleOutlineOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';

/** The icon of a status: its shape differs, so the status never rests on colour alone. */
export function StatusIcon({ status, fontSize = 'small' }: { status: string; fontSize?: 'inherit' | 'small' }) {
  switch (status) {
    case 'pass':
      return <CheckCircleIcon fontSize={fontSize} />;
    case 'warning':
      return <WarningIcon fontSize={fontSize} />;
    case 'fail':
      return <ErrorIcon fontSize={fontSize} />;
    default:
      return <RemoveCircleIcon fontSize={fontSize} />;
  }
}
