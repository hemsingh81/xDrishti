import { SearchField } from '@/shared/ui/SearchField';

interface SearchBoxProps {
  /** The committed search text (from the URL). */
  value: string;
  onCommit: (value: string) => void;
}

/** The master list's search field: commits to the URL 300 ms after the last keystroke; follows the URL (Back, clear all). */
export function SearchBox({ value, onCommit }: SearchBoxProps) {
  return (
    <SearchField
      value={value}
      onCommit={onCommit}
      label="Search instruments"
      placeholder="Search symbol, name or ISIN"
    />
  );
}
