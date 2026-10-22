// The slim entry of Monaco (editor core only, no built-in languages; the package maps `editor/editor.api` to
// `esm/vs/editor/editor.api.js`) has no types of its own: it has those of the package.
declare module 'monaco-editor/editor/editor.api' {
  export * from 'monaco-editor';
}
