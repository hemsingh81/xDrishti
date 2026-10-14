import PersonIcon from '@mui/icons-material/PersonOutlined';
import { useQuery } from '@tanstack/react-query';
import { formatDate } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { accountQuery } from '../api/accountApi';
import { AccountCard } from './AccountCard';
import { ProfileForm } from './ProfileForm';
import { QueryAlert } from './QueryAlert';

/** Display name and sign-in e-mail. Saving asks for the password (step-up) when the API wants a fresh check. */
export function ProfileCard() {
  const query = useQuery(accountQuery);
  const account = query.data;
  return (
    <AccountCard
      id="profile"
      title="Profile"
      icon={PersonIcon}
      hue="accounts"
      description={
        account
          ? `The name and e-mail you sign in with. Owner account created ${formatDate(account.createdAt)}.`
          : 'The name and e-mail you sign in with.'
      }
    >
      {query.isPending && <LoadingState label="Loading your profile" />}
      {query.isError && (
        <QueryAlert
          error={query.error}
          fallback="Your profile could not be loaded."
          onRetry={() => {
            void query.refetch();
          }}
        />
      )}
      {account && <ProfileForm account={account} />}
    </AccountCard>
  );
}
