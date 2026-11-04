import type { ChangeEvent } from 'react';

interface FieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  /** Validation message from the API, shown next to the field. */
  error?: string;
  hint?: string;
  multiline?: boolean;
  disabled?: boolean;
}

/** A labelled text input or textarea with an optional hint and an error message tied to it for assistive tech. */
export function Field({ id, label, value, onChange, error, hint, multiline, disabled }: FieldProps) {
  const hintId = hint ? `${id}-hint` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined;
  const handleChange = (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => onChange(event.target.value);
  const common = {
    id,
    name: id,
    value,
    onChange: handleChange,
    disabled,
    'aria-invalid': error ? true : undefined,
    'aria-describedby': describedBy,
  } as const;

  return (
    <div className={`field${error ? ' field--invalid' : ''}`}>
      <label htmlFor={id}>{label}</label>
      {hint && (
        <p id={hintId} className="field-hint">
          {hint}
        </p>
      )}
      {multiline ? <textarea rows={4} {...common} /> : <input type="text" {...common} />}
      {error && (
        <p id={errorId} className="field-error">
          {error}
        </p>
      )}
    </div>
  );
}
