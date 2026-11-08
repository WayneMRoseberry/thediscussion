import { useEffect, useRef, useState, type FormEvent } from 'react';
import { ApiError, deleteStatement, updateGroup, updateStatement } from '../api/client';
import type { Discussion, Statement, SupportGroupType, ThesisStatus, UpdateGroupInput } from '../api/types';
import {
  buildOutline,
  countBeneath,
  deleteQuestion,
  flattenBeneath,
  groupLabel,
  groupTypeLabels,
  isUnnamedGroup,
  kindLabels,
  markerFor,
  maxNestedLevels,
  statementCount,
  statementNames,
  unnamedGroupName,
  type OutlineNode,
} from '../outline/outline';
import { ConfirmDialog } from './ConfirmDialog';
import { Field } from './Field';
import { GroupTypeField } from './GroupTypeField';

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

/** The row control that opened an edit form, so focus can go back to it when the form closes. */
type Opener = 'edit' | 'addName';

/**
 * The discussion as an indented outline: thesis, support groups (nested under the thesis, under other groups, or under
 * supporting statements as sub-arguments), statements and their challenges. Every statement with text can be edited
 * in place, every group's name and type can be edited, and every statement except the thesis can be deleted (after a
 * confirmation).
 */
export function Outline({ discussion, onChanged }: OutlineProps) {
  const [editing, setEditing] = useState<{ id: string; opener: Opener } | null>(null);
  const [focus, setFocus] = useState<{ id: string; opener: Opener } | null>(null);
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
    editingId: editing?.id ?? null,
    focus,
    startEdit: (id, opener) => setEditing({ id, opener }),
    stopEdit: (id, announcement) => {
      setFocus({ id, opener: editing?.id === id ? editing.opener : 'edit' });
      setEditing(null);
      if (announcement) onChanged(announcement);
    },
    focused: () => setFocus(null),
    startDelete: (statement) => {
      setDeleteError(undefined);
      setDeleting(statement);
    },
  };

  return (
    <>
      <ul className="outline" aria-label="Argument outline">
        <OutlineItem node={root} level={1} status={discussion.status} actions={actions} />
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
  /** The row control that should take focus again (after its editor closed). */
  focus: { id: string; opener: Opener } | null;
  startEdit: (id: string, opener: Opener) => void;
  stopEdit: (id: string, announcement?: string) => void;
  focused: () => void;
  startDelete: (statement: Statement) => void;
}

interface OutlineItemProps {
  node: OutlineNode;
  /** 1 for the thesis. */
  level: number;
  status: ThesisStatus;
  actions: RowActions;
}

/** One statement and, nested beneath it, everything under it. */
function OutlineItem({ node, level, status, actions }: OutlineItemProps) {
  const { statement } = node;

  return (
    <li className={`outline-item outline-item--${statement.kind}`} data-statement-id={statement.id}>
      <OutlineRow statement={statement} status={status} actions={actions} />
      {node.children.length > 0 &&
        (level < maxNestedLevels ? (
          <ul>
            {node.children.map((child) => (
              <OutlineItem key={child.statement.id} node={child} level={level + 1} status={status} actions={actions} />
            ))}
          </ul>
        ) : (
          <ul className="outline-flat">
            {flattenBeneath(node).map(({ node: descendant, depth }) => (
              <li
                key={descendant.statement.id}
                className={`outline-item outline-item--${descendant.statement.kind}`}
                data-statement-id={descendant.statement.id}
                aria-level={level + depth}
                style={{ marginLeft: `${(depth - 1) * 2}rem` }}
              >
                <OutlineRow statement={descendant.statement} status={status} actions={actions} />
              </li>
            ))}
          </ul>
        ))}
    </li>
  );
}

/** A statement's row: the statement and its controls, or its edit form while it is being edited. */
function OutlineRow({ statement, status, actions }: { statement: Statement; status: ThesisStatus; actions: RowActions }) {
  const marker = markerFor(statement);
  const glyph = statement.kind === 'counterArgument' || statement.kind === 'rebuttal' ? 'x' : statement.kind === 'supportGroup' ? '+' : null;
  const isGroup = statement.kind === 'supportGroup';
  const unnamed = isUnnamedGroup(statement);
  const editButton = useRef<HTMLButtonElement>(null);
  const addNameButton = useRef<HTMLButtonElement>(null);
  const focusOpener = actions.focus?.id === statement.id ? actions.focus.opener : null;
  const { focused } = actions;

  useEffect(() => {
    if (focusOpener) {
      // "Add a name" is gone once the group has a name; the Edit button is always there.
      (focusOpener === 'addName' ? (addNameButton.current ?? editButton.current) : editButton.current)?.focus();
      focused();
    }
  }, [focusOpener, focused]);

  const close = () => actions.stopEdit(statement.id);
  const saved = () => actions.stopEdit(statement.id, `Saved the ${statementNames[statement.kind]}.`);

  if (actions.editingId === statement.id) {
    return isGroup ? (
      <EditGroupForm discussionId={actions.discussionId} group={statement} onCancel={close} onSaved={saved} />
    ) : (
      <EditStatementForm discussionId={actions.discussionId} statement={statement} onCancel={close} onSaved={saved} />
    );
  }

  return (
    <div className="outline-row">
      <div className="outline-node">
        {glyph && (
          <>
            <span className="outline-glyph" aria-hidden="true">
              {glyph}
            </span>{' '}
          </>
        )}
        {isGroup ? (
          <>
            <span className="kind-label">{statement.groupType ? groupTypeLabels[statement.groupType] : 'SUPPORT GROUP'}:</span>{' '}
            <span className={`group-name${unnamed ? ' group-name--unnamed' : ''}`}>{statement.name || unnamedGroupName}</span>
            {unnamed && (
              <>
                {' '}
                <button
                  type="button"
                  className="link-button"
                  ref={addNameButton}
                  onClick={() => actions.startEdit(statement.id, 'addName')}
                >
                  Add a name<span className="visually-hidden"> to {describeForLabel(statement)}</span>
                </button>
              </>
            )}
          </>
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
        <button type="button" className="link-button" ref={editButton} onClick={() => actions.startEdit(statement.id, 'edit')}>
          Edit<span className="visually-hidden"> {describeForLabel(statement)}</span>
        </button>
        {statement.kind !== 'thesis' && (
          <button type="button" className="link-button link-button--danger" onClick={() => actions.startDelete(statement)}>
            Delete<span className="visually-hidden"> {describeForLabel(statement)}</span>
          </button>
        )}
      </div>
    </div>
  );
}

/** Which statement a row's Edit or Delete button acts on, for screen readers: "counter argument: Slack pings...". */
function describeForLabel(statement: Statement): string {
  if (statement.kind === 'supportGroup') {
    return `support group (${groupLabel(statement)})`;
  }
  const text = statement.text ?? '';
  return `${statementNames[statement.kind]}: ${text.length > 60 ? `${text.slice(0, 57)}...` : text}`;
}

/** Field errors shown in the form-level alert, because the form has no control for them. */
function errorsNotShownInline(error: ApiError | null, inline: string[]): string[] {
  return error
    ? Object.entries(error.fieldErrors)
        .filter(([field]) => !inline.includes(field))
        .flatMap(([, messages]) => messages)
    : [];
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
  const otherFieldErrors = errorsNotShownInline(error, ['text']);

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

interface EditGroupFormProps {
  discussionId: string;
  group: Statement;
  onCancel: () => void;
  onSaved: () => void;
}

/**
 * Edits a support group's name and type in place (a group has no text). Only changed fields are sent. API errors
 * appear next to their field; failures without field errors offer Retry.
 */
function EditGroupForm({ discussionId, group, onCancel, onSaved }: EditGroupFormProps) {
  const currentName = group.name ?? '';
  const currentType: SupportGroupType = group.groupType ?? 'allRequired';
  const [groupName, setGroupName] = useState(currentName);
  const [groupType, setGroupType] = useState<SupportGroupType>(currentType);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const idPrefix = `edit-${group.id}`;
  const hasFieldErrors = error !== null && Object.keys(error.fieldErrors).length > 0;
  const otherFieldErrors = errorsNotShownInline(error, ['name', 'groupType']);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const changes: UpdateGroupInput = {
      ...(groupName !== currentName ? { name: groupName } : {}),
      ...(groupType !== group.groupType ? { groupType } : {}),
    };
    if (Object.keys(changes).length === 0) {
      onCancel();
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      await updateGroup(discussionId, group.id, changes);
      onSaved();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError(0, 'Something went wrong. Try again.'));
      setSubmitting(false);
    }
  }

  return (
    <form className="edit-form" aria-label="Edit support group" onSubmit={handleSubmit} noValidate>
      {error && !hasFieldErrors && (
        <div role="alert" className="error-banner">
          <p>Could not save the support group. {error.message}</p>
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
        id={`${idPrefix}-name`}
        label="Name"
        value={groupName}
        onChange={setGroupName}
        error={error?.fieldError('name')}
        hint="Required. Up to 100 characters."
        disabled={submitting}
        autoFocus
      />
      <GroupTypeField
        id={`${idPrefix}-group-type`}
        value={groupType}
        onChange={setGroupType}
        error={error?.fieldError('groupType')}
        disabled={submitting}
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
