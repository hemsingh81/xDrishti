import { Box, useTheme } from '@mui/material';
import * as monaco from 'monaco-editor/editor/editor.api';
import { configureMonacoYaml, type MonacoYaml } from 'monaco-yaml';
import { useEffect, useRef } from 'react';
import type { EditorMarker } from '../lib/markers';

export interface YamlEditorProps {
  value: string;
  onChange: (value: string) => void;
  /** The JSON Schema of the file as text; absent while it is loading or when it could not be read. */
  schema?: string;
  markers: readonly EditorMarker[];
  label: string;
}

// The workers are bundled by Vite from the packages (no CDN): one for the editor core, one for YAML.
globalThis.MonacoEnvironment = {
  getWorker: (_id, label) =>
    label === 'yaml'
      ? new Worker(new URL('monaco-yaml/yaml.worker.js', import.meta.url), { type: 'module' })
      : new Worker(new URL('monaco-editor/editor/editor.worker.js', import.meta.url), { type: 'module' }),
};

const modelUri = monaco.Uri.parse('inmemory://xd/strategy.yaml');
const markerOwner = 'xd-lab';
const severities = {
  error: monaco.MarkerSeverity.Error,
  warning: monaco.MarkerSeverity.Warning,
  info: monaco.MarkerSeverity.Info,
} as const;

let yaml: MonacoYaml | undefined;
function applySchema(schema: string | undefined) {
  const options = {
    validate: true,
    completion: true,
    hover: true,
    enableSchemaRequest: false,
    schemas: schema
      ? [
          {
            uri: 'inmemory://xd/strategy.schema.json',
            fileMatch: ['*', '**/strategy.yaml'],
            schema: JSON.parse(schema) as object,
          },
        ]
      : [],
  };
  if (yaml) void yaml.update(options);
  else yaml = configureMonacoYaml(monaco, options);
}

/** Monaco with the YAML language service and the served schema. Loaded lazily by the page. */
export default function MonacoYamlEditor({ value, onChange, schema, markers, label }: YamlEditorProps) {
  const host = useRef<HTMLDivElement>(null);
  const editor = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
  const onChangeRef = useRef(onChange);
  const mode = useTheme().palette.mode;

  useEffect(() => {
    onChangeRef.current = onChange;
  });

  useEffect(() => {
    if (!host.current) return undefined;
    const model = monaco.editor.getModel(modelUri) ?? monaco.editor.createModel(value, 'yaml', modelUri);
    model.setValue(value);
    const created = monaco.editor.create(host.current, {
      model,
      ariaLabel: label,
      automaticLayout: true,
      minimap: { enabled: false },
      scrollBeyondLastLine: false,
      tabSize: 2,
      insertSpaces: true,
      fontSize: 13,
    });
    const subscription = created.onDidChangeModelContent(() => {
      onChangeRef.current(created.getValue());
    });
    editor.current = created;
    return () => {
      subscription.dispose();
      created.dispose();
      model.dispose();
      editor.current = null;
    };
    // The editor is created once; value, schema and markers are synced by the effects below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (editor.current && editor.current.getValue() !== value) editor.current.setValue(value);
  }, [value]);

  useEffect(() => {
    applySchema(schema);
  }, [schema]);

  useEffect(() => {
    const model = editor.current?.getModel();
    if (model) {
      monaco.editor.setModelMarkers(
        model,
        markerOwner,
        markers.map((m) => ({ ...m, severity: severities[m.severity] })),
      );
    }
  }, [markers]);

  useEffect(() => {
    monaco.editor.setTheme(mode === 'dark' ? 'vs-dark' : 'vs');
  }, [mode]);

  return (
    <Box ref={host} sx={{ height: 420, border: 1, borderColor: 'divider', borderRadius: 1, overflow: 'hidden' }} />
  );
}
