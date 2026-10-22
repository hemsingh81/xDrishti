interface EditorStubProps {
  value: string;
  onChange: (value: string) => void;
  label: string;
}

/** Stand-in for the lazy Monaco editor of the Lab (jsdom cannot run it): a textarea with the same label and contract. */
export default function EditorStub({ value, onChange, label }: EditorStubProps) {
  return (
    <textarea
      aria-label={label}
      value={value}
      onChange={(e) => {
        onChange(e.target.value);
      }}
    />
  );
}
