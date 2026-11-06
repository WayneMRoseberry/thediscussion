import { useId, useState, type FormEvent } from 'react';
import { addStatement, ApiError } from '../api/client';
import type { AddableKind, Discussion, Statement, SupportGroupType } from '../api/types';
import { allowedParentKinds, describeStatement, groupTypeLabels, kindNames } from '../outline/outline';
import { Field } from './Field';

const addableKinds: { kind: AddableKind; button: string }[] = [
  { kind: 'supportGroup', button: 'Add support group' },
  { kind: 'supportingStatement', button: 'Add support' },
  { kind: 'illustration', button: 'Add illustration' },
  { kind: 'counterArgument', button: 'Add counter' },
  { kind: 'rebuttal', button: 'Add rebuttal' },
];

const targetLabels: Record<AddableKind, string> = {
  supportGroup: 'Supports',
  supportingStatement: 'Support group',
  illustration: 'Illustrates',
  counterArgument: 'Counters',
  rebuttal: 'Rebuts',
};

const missingTargetHints: Record<AddableKind, string> = {
  supportGroup: 'There is no thesis to support.',
  supportingStatement: 'Add a support group first.',
  illustration: 'There is nothing to illustrate yet.',
  counterArgument: 'There is nothing to counter yet.',
  rebuttal: 'Add a counter argument first.',
};

interface AddStatementPanelProps {
  discussion: Discussion;
  /** Called after the API accepted a statement, with a message to announce, so the page can reload the outline and statuses. */
  onAdded: (announcement: string) => void;
}

/** Buttons that open a form for each statement kind. */
export function AddStatementPanel({ discussion, onAdded }: AddStatementPanelProps) {
  const [activeKind, setActiveKind] = useState<AddableKind | null>(null);
  const formId = useId();

  return (
    <section className="add-panel" aria-labelledby={`${formId}-heading`}>
      <h2 id={`${formId}-heading`}>Add to the discussion</h2>
      <div className="add-buttons">
        {addableKinds.map(({ kind, button }) => (
          <button
            key={kind}
            type="button"
            className={`add-button${activeKind === kind ? ' add-button--active' : ''}`}
            aria-expanded={activeKind === kind}
            aria-controls={activeKind === kind ? formId : undefined}
            onClick={() => {
              setActiveKind(activeKind === kind ? null : kind);
            }}
          >
            <span aria-hidden="true">+ </span>
            {button}
          </button>
        ))}
      </div>
      {activeKind && (
        <AddStatementForm
          key={activeKind}
          id={formId}
          kind={activeKind}
          discussion={discussion}
          onCancel={() => setActiveKind(null)}
          onAdded={() => {
            setActiveKind(null);
            onAdded(`Added the ${kindNames[activeKind]}.`);
          }}
        />
      )}
    </section>
  );
}

interface AddStatementFormProps {
  id: string;
  kind: AddableKind;
  discussion: Discussion;
  onCancel: () => void;
  onAdded: () => void;
}

function AddStatementForm({ id, kind, discussion, onCancel, onAdded }: AddStatementFormProps) {
  const targets = discussion.statements.filter((statement) => allowedParentKinds[kind].includes(statement.kind));
  const [parentId, setParentId] = useState(targets[0]?.id ?? '');
  const [text, setText] = useState('');
  const [groupType, setGroupType] = useState<SupportGroupType>('allRequired');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const name = kindNames[kind];
  const headingId = `${id}-title`;
  const targetErrorId = `${id}-target-error`;
  const groupErrorId = `${id}-group-error`;
  const targetError = error?.fieldError('parentId');
  const groupError = error?.fieldError('groupType');
  const otherFieldErrors = error
    ? Object.entries(error.fieldErrors)
        .filter(([field]) => !['parentId', 'groupType', 'text'].includes(field))
        .flatMap(([, messages]) => messages)
    : [];
  const formError = error && (Object.keys(error.fieldErrors).length === 0 ? error.message : otherFieldErrors.join(' '));

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await addStatement(
        discussion.id,
        kind === 'supportGroup' ? { kind, parentId, groupType } : { kind, parentId, text },
      );
      onAdded();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError(0, 'Something went wrong. Try again.'));
      setSubmitting(false);
    }
  }

  return (
    <form id={id} className="add-form" aria-labelledby={headingId} onSubmit={handleSubmit} noValidate>
      <h3 id={headingId}>Add {name}</h3>
      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}

      <div className={`field${targetError ? ' field--invalid' : ''}`}>
        {targets.length === 0 ? (
          <p className="field-hint">{missingTargetHints[kind]}</p>
        ) : (
          <>
          <label htmlFor={`${id}-target`}>{targetLabels[kind]}</label>
          <select
            id={`${id}-target`}
            value={parentId}
            onChange={(event) => setParentId(event.target.value)}
            aria-invalid={targetError ? true : undefined}
            aria-describedby={targetError ? targetErrorId : undefined}
          >
            {targets.map((target) => (
              <option key={target.id} value={target.id}>
                {targetLabel(target, discussion.statements)}
              </option>
            ))}
          </select>
          </>
        )}
        {targetError && (
          <p id={targetErrorId} className="field-error">
            {targetError}
          </p>
        )}
      </div>

      {kind === 'supportGroup' ? (
        <fieldset
          className={`field${groupError ? ' field--invalid' : ''}`}
          aria-describedby={groupError ? groupErrorId : undefined}
        >
          <legend>Group type</legend>
          {(['allRequired', 'anySufficient'] as const).map((type) => (
            <label key={type} className="radio">
              <input
                type="radio"
                name={`${id}-group-type`}
                value={type}
                checked={groupType === type}
                onChange={() => setGroupType(type)}
              />{' '}
              {type === 'allRequired' ? 'All required: every support must stand' : 'Any sufficient: one standing support is enough'}
              <span className="visually-hidden"> ({groupTypeLabels[type]})</span>
            </label>
          ))}
          {groupError && (
            <p id={groupErrorId} className="field-error">
              {groupError}
            </p>
          )}
        </fieldset>
      ) : (
        <Field
          id={`${id}-text`}
          label="Text"
          value={text}
          onChange={setText}
          error={error?.fieldError('text')}
          hint="Up to 5000 characters."
          multiline
          disabled={submitting}
        />
      )}

      <div className="form-actions">
        <button type="submit" disabled={submitting || targets.length === 0}>
          {submitting ? 'Posting...' : `Post ${name}`}
        </button>
        <button type="button" className="button-secondary" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  );
}

function targetLabel(target: Statement, statements: Statement[]): string {
  if (target.kind !== 'supportGroup') {
    return describeStatement(target);
  }
  const groups = statements.filter((statement) => statement.kind === 'supportGroup');
  const number = groups.findIndex((group) => group.id === target.id) + 1;
  return `Support group ${number}: ${target.groupType ? groupTypeLabels[target.groupType] : ''}`;
}
