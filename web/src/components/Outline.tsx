import { useEffect, useRef, useState, type FormEvent } from 'react';
import { ApiError, deleteStatement, updateStatement } from '../api/client';
import type { Discussion, Statement, ThesisStatus } from '../api/types';
import {
  buildOutline,
  countBeneath,
  deleteQuestion,
  groupTypeLabels,
  kindLabels,
  markerFor,
  statementCount,
  statementNames,
  type OutlineNode,
} from '../outline/outline';
import { ConfirmDialog } from './ConfirmDialog';
import { Field } from './Field';

const statusLabels: Record<ThesisStatus, string> = {
  unsupported: 'UNSUPPORTED',
  open: 'OPEN',
  countered: 'COUNTERED',
  tested: 'TESTED',
};

export function StatusBadge({ status }: { status: ThesisStatus }) {
  return (
    <span className={`status-badge status-badge--${status}`}>
      <span className="visually-hidden">Status: </span>
      {statusLabels[status]}
    </span>
  );
}

interface OutlineProps {
  discussion: Discussion;
  /** Called after a statement was edited or deleted, with a message to announce, so the page can reload. */
  onChanged: (announcement: string) => void;
}

/**
 * The discussion as an indented outline: thesis, support groups, statements and their challenges. Every statement
 * with text can be edited in place, and every statement except the thesis can be deleted (after a confirmation).
 */
export function Outline({ discussion, onChanged }: OutlineProps) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [focusId, setFocusId] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<Statement | null>(null);
  const [deleteBusy, setDeleteBusy] = useState(false);
  const [deleteError, setDeleteError] = useState<string | undefined>();

  const root = buildOutline(discussion.statements, discussion.thesisId);
  if (!root) {
    return <p>This discussion has no thesis.</p>;
  }

  const beneath = deleting ? countBeneath(discussion.statements, deleting.id) : 0;

  async function confirmDelete() {
    if (!deleting) return;
    setDeleteBusy(true);
    setDeleteError(undefined);
    try {
      await deleteStatement(discussion.id, deleting.id);
      const name = statementNames[deleting.kind];
      setDeleting(null);
      setDeleteBusy(false);
      onChanged(beneath === 0 ? `Deleted the ${name}.` : `Deleted the ${name} and ${statementCount(beneath)} beneath it.`);
    } catch (caught) {
      setDeleteError(`Could not delete. ${caught instanceof Error ? caught.message : 'Something went wrong.'}`);
      setDeleteBusy(false);
    }
  }

  const actions: RowActions = {
    discussionId: discussion.id,
    editingId,
    focusId,
    startEdit: (id) => setEditingId(id),
    stopEdit: (id, announcement) => {
      setEditingId(null);
      setFocusId(id);
      if (announcement) onChanged(announcement);
    },
    focused: () => setFocusId(null),
    startDelete: (statement) => {
      setDeleteError(undefined);
      setDeleting(statement);
    },
  };

  return (
    <>
      <ul className="outline" aria-label="Argument outline">
        <OutlineItem node={root} status={discussion.status} actions={actions} />
      </ul>
      {deleting && (
        <ConfirmDialog
          title={`Delete ${statementNames[deleting.kind]}`}
          message={`${deleteQuestion(deleting, beneath)} This cannot be undone.`}
          confirmLabel="Delete"
          busyLabel="Deleting..."
          busy={deleteBusy}
          error={deleteError}
          onConfirm={() => void confirmDelete()}
          onCancel={() => setDeleting(null)}
        />
      )}
    </>
  );
}

interface RowActions {
  discussionId: string;
  editingId: string | null;
  /** The row whose Edit button should take focus again (after its editor closed). */
  focusId: string | null;
  startEdit: (id: string) => void;
  stopEdit: (id: string, announcement?: string) => void;
  focused: () => void;
  startDelete: (statement: Statement) => void;
}

function OutlineItem({ node, status, actions }: { node: OutlineNode; status: ThesisStatus; actions: RowActions }) {
  const { statement } = node;
  const marker = markerFor(statement);
  const glyph = statement.kind === 'counterArgument' || statement.kind === 'rebuttal' ? 'x' : statement.kind === 'supportGroup' ? '+' : null;
  const editable = statement.kind !== 'supportGroup';
  const editButton = useRef<HTMLButtonElement>(null);
  const shouldFocus = actions.focusId === statement.id;
  const { focused } = actions;

  useEffect(() => {
    if (shouldFocus) {
      editButton.current?.focus();
      focused();
    }
  }, [shouldFocus, focused]);

  return (
    <li className={`outline-item outline-item--${statement.kind}`} data-statement-id={statement.id}>
      {actions.editingId === statement.id ? (
        <EditStatementForm
          discussionId={actions.discussionId}
          statement={statement}
          onCancel={() => actions.stopEdit(statement.id)}
          onSaved={() => actions.stopEdit(statement.id, `Saved the ${statementNames[statement.kind]}.`)}
        />
      ) : (
        <div className="outline-row">
          <div className="outline-node">
            {glyph && (
              <>
                <span className="outline-glyph" aria-hidden="true">
                  {glyph}
                </span>{' '}
              </>
            )}
            {statement.kind === 'supportGroup' ? (
              <span className="kind-label">
                Support group: {statement.groupType ? groupTypeLabels[statement.groupType] : ''}
              </span>
            ) : (
              <>
                <span className="kind-label">[{kindLabels[statement.kind]}]</span>{' '}
                <span className="statement-text">{statement.text}</span>
              </>
            )}
            {statement.editedAt && (
              <>
                {' '}
                <span className="edited-marker" title={`Edited ${new Date(statement.editedAt).toLocaleString()}`}>
                  (edited)
                </span>
              </>
            )}
            {statement.kind === 'thesis' && (
              <>
                {' '}
                <StatusBadge status={status} />
              </>
            )}
            {marker && (
              <>
                {' '}
                <span className={`marker marker--${marker.tone}`}>({marker.text})</span>
              </>
            )}
          </div>
          <div className="outline-actions">
            {editable && (
              <button type="button" className="link-button" ref={editButton} onClick={() => actions.startEdit(statement.id)}>
                Edit<span className="visually-hidden"> {describeForLabel(statement)}</span>
              </button>
            )}
            {statement.kind !== 'thesis' && (
              <button type="button" className="link-button link-button--danger" onClick={() => actions.startDelete(statement)}>
                Delete<span className="visually-hidden"> {describeForLabel(statement)}</span>
              </button>
            )}
          </div>
        </div>
      )}
      {node.children.length > 0 && (
        <ul>
          {node.children.map((child) => (
            <OutlineItem key={child.statement.id} node={child} status={status} actions={actions} />
          ))}
        </ul>
      )}
    </li>
  );
}

/** Which statement a row's Edit or Delete button acts on, for screen readers: "counter argument: Slack pings...". */
function describeForLabel(statement: Statement): string {
  if (statement.kind === 'supportGroup') {
    return `support group (${statement.groupType ? groupTypeLabels[statement.groupType] : ''})`;
  }
  const text = statement.text ?? '';
  return `${statementNames[statement.kind]}: ${text.length > 60 ? `${text.slice(0, 57)}...` : text}`;
}

interface EditStatementFormProps {
  discussionId: string;
  statement: Statement;
  onCancel: () => void;
  onSaved: () => void;
}

/** Edits a statement's text in place, in its outline row. API errors appear next to the field; failures offer Retry. */
function EditStatementForm({ discussionId, statement, onCancel, onSaved }: EditStatementFormProps) {
  const [text, setText] = useState(statement.text ?? '');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const name = statementNames[statement.kind];
  const fieldId = `edit-${statement.id}-text`;
  const hasFieldErrors = error !== null && Object.keys(error.fieldErrors).length > 0;
  const otherFieldErrors = error
    ? Object.entries(error.fieldErrors)
        .filter(([field]) => field !== 'text')
        .flatMap(([, messages]) => messages)
    : [];

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await updateStatement(discussionId, statement.id, text);
      onSaved();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError(0, 'Something went wrong. Try again.'));
      setSubmitting(false);
    }
  }

  return (
    <form className="edit-form" aria-label={`Edit ${name}`} onSubmit={handleSubmit} noValidate>
      {error && !hasFieldErrors && (
        <div role="alert" className="error-banner">
          <p>Could not save the {name}. {error.message}</p>
          <button type="submit" disabled={submitting}>
            Retry
          </button>
        </div>
      )}
      {otherFieldErrors.length > 0 && (
        <p role="alert" className="form-error">
          {otherFieldErrors.join(' ')}
        </p>
      )}
      <Field
        id={fieldId}
        label="Text"
        value={text}
        onChange={setText}
        error={error?.fieldError('text')}
        hint="Up to 5000 characters."
        multiline
        disabled={submitting}
        autoFocus
      />
      <div className="form-actions">
        <button type="submit" disabled={submitting}>
          {submitting ? 'Saving...' : 'Save'}
        </button>
        <button type="button" className="button-secondary" onClick={onCancel} disabled={submitting}>
          Cancel
        </button>
      </div>
    </form>
  );
}
