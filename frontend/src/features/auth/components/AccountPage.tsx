import ManageAccountsIcon from '@mui/icons-material/ManageAccountsOutlined';
import { Stack } from '@mui/material';
import { PageHeader } from '@/shared/ui';
import { ToastProvider } from '@/shared/ui/ToastProvider';
import { ChangePasswordCard } from './ChangePasswordCard';
import { ProfileCard } from './ProfileCard';
import { SignInActivityCard } from './SignInActivityCard';

/** My account: the owner's name and e-mail, the password and recent sign-in activity. Opened from the user menu. */
export function AccountPage() {
  return (
    <ToastProvider>
      <PageHeader
        title="My account"
        icon={ManageAccountsIcon}
        hue="accounts"
        description="Your sign-in details. Changing the name or e-mail asks for your password; changing the password signs out your other devices."
      />
      <Stack spacing={2} sx={{ maxWidth: 900 }}>
        <ProfileCard />
        <ChangePasswordCard />
        <SignInActivityCard />
      </Stack>
    </ToastProvider>
  );
}
