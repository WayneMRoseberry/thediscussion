import type { SupportGroupType } from '../api/types';
import { groupTypeLabels } from '../outline/outline';

const groupTypes: { type: SupportGroupType; description: string }[] = [
  { type: 'allRequired', description: 'All required: every support must stand' },
  { type: 'anySufficient', description: 'Any sufficient: one standing support is enough' },
];

interface GroupTypeFieldProps {
  id: string;
  value: SupportGroupType;
  onChange: (value: SupportGroupType) => void;
  /** Validation message from the API, shown next to the choice. */
  error?: string;
  disabled?: boolean;
}

/** The group type choice (a radio group) with an error message tied to it for assistive tech. */
export function GroupTypeField({ id, value, onChange, error, disabled }: GroupTypeFieldProps) {
  const errorId = `${id}-error`;

  return (
    <fieldset className={`field${error ? ' field--invalid' : ''}`} aria-describedby={error ? errorId : undefined}>
      <legend>Group type</legend>
      {groupTypes.map(({ type, description }) => (
        <label key={type} className="radio">
          <input
            type="radio"
            name={id}
            value={type}
            checked={value === type}
            onChange={() => onChange(type)}
            disabled={disabled}
            aria-invalid={error ? true : undefined}
          />{' '}
          {description}
          <span className="visually-hidden"> ({groupTypeLabels[type]})</span>
        </label>
      ))}
      {error && (
        <p id={errorId} className="field-error">
          {error}
        </p>
      )}
    </fieldset>
  );
}
