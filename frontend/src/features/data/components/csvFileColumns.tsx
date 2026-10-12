import type { CsvFileRow } from '@/shared/api';
import { formatIstDateTime } from '@/shared/lib/format';
import type { GridColumn } from '@/shared/ui/grid';
import { formatSize } from '../lib/csvReview';
import { FileNameCell, LoadedCell, MappingCell, SelectCell, UndoCell } from './csvFileCells';
import { CsvStateChip } from './CsvStateChip';

/**
 * The columns of the review list: a checkbox (not for a file that cannot be loaded), the file, its state, the instrument,
 * size, modified time, what was loaded and Undo for loaded files. Size is hidden below `sm`, modified time and the loaded
 * column below `md`. The choice reaches the checkboxes through `CsvListContext`, so this array never changes.
 */
export const csvFileColumns: readonly GridColumn<CsvFileRow>[] = [
  { id: 'select', header: 'Select', kind: 'handle', width: 64, render: (file) => <SelectCell file={file} /> },
  { id: 'name', header: 'File', grow: true, minWidth: 220, render: (file) => <FileNameCell file={file} /> },
  { id: 'state', header: 'State', minWidth: 110, render: (file) => <CsvStateChip state={file.state} /> },
  { id: 'instrument', header: 'Instrument', minWidth: 160, render: (file) => <MappingCell file={file} /> },
  { id: 'size', header: 'Size', numeric: true, hideBelow: 'sm', accessor: (file) => formatSize(file.size) },
  { id: 'modified', header: 'Modified (IST)', hideBelow: 'md', accessor: (file) => formatIstDateTime(file.modifiedAt) },
  { id: 'loaded', header: 'Loaded', hideBelow: 'md', minWidth: 200, render: (file) => <LoadedCell file={file} /> },
  { id: 'undo', header: 'Undo', kind: 'handle', width: 80, render: (file) => <UndoCell file={file} /> },
];
