export { BrandMark } from './BrandMark';
export { PageHeader } from './PageHeader';
export { ErrorState, LoadingState } from './QueryState';
export { StatTile } from './StatTile';
export { StatusChip } from './StatusChip';
// Heavier widgets used only by lazy screens are imported by path (@/shared/ui/dialog, @/shared/ui/ConfirmDialog,
// @/shared/ui/ToastProvider, @/shared/ui/toastContext) so that this barrel does not pull Dialog and Snackbar into the
// initial bundle.
