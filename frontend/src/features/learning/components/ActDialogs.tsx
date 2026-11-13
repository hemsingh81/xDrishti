import { lazy } from 'react';
import { loadApplyRollbackDialog, loadHoldOutScoreDialog, loadMlSwitchDialog } from '@/features/owner-acts';

// The owner acts of the Models screen live in `owner-acts`, the one writer feature, and load only when the owner asks for one.
// This file only draws them; the screen itself holds no write.
export const ApplyRollbackDialog = lazy(async () => ({ default: await loadApplyRollbackDialog() }));
export const MlSwitchDialog = lazy(async () => ({ default: await loadMlSwitchDialog() }));
export const HoldOutScoreDialog = lazy(async () => ({ default: await loadHoldOutScoreDialog() }));
