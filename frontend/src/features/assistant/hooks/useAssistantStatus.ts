import { useQuery } from '@tanstack/react-query';
import { statusQuery } from '../api/assistantApi';

export function useAssistantStatus() {
  return useQuery(statusQuery);
}
