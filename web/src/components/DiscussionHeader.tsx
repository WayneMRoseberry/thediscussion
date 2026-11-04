import { useRef, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';
import { ApiError, deleteDiscussion, updateDiscussion } from '../api/client';
import type { Discussion } from '../api/types';
import { statementCount } from '../outline/outline';
import { formatTags, parseTags } from '../tags/tags';
import { ConfirmDialog } from './ConfirmDialog';
import { Field } from './Field';
import { TagLinks } from './TagLinks';

/** Router state the discussion list reads to announce what just happened on another page. */
export interface ListPageState {
  announcement?: string;
}

interface DiscussionHeaderProps {
  discussion: Discussion;
  /** Called after the title or tags were saved, with a message to announce, so the page can reload. */
  onChanged: (announcement: string) => void;
}

/** The discussion's title and tags (as links), with Edit and Delete for the discussion. */
export function DiscussionHeader({ discussion, onChanged }: DiscussionHeaderProps) {
  const navigate = useNavigate();
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [deleteBusy, setDeleteBusy] = useState(false);
  const [deleteError, setDeleteError] = useState<string | undefined>();
  const editButton = useRef<HTMLButtonElement>(null);

  async function confirmDelete() {
    setDeleteBusy(true);
    setDeleteError(undefined);
    try {
      await deleteDiscussion(discussion.id);
      const state: ListPageState = { announcement: `Deleted the discussion "${discussion.title}".` };
      navigate('/', { state });
    } catch (caught) {
      setDeleteError(`Could not delete the discussion. ${caught instanceof Error ? caught.message : 'Something went wrong.'}`);
      setDeleteBusy(false);
    }
  }

  function closeEditor(announcement?: string) {
    setEditing(false);
    // The Edit button comes back once the form closes; return focus to it.
    setTimeout(() => editButton.current?.focus(), 0);
    if (announcement) onChanged(announcement);
  }

  return (
    <header className="discussion-header">
      {editing ? (
        <EditDiscussionForm discussion={discussion} onCancel={() => closeEditor()} onSaved={() => closeEditor('Saved the discussion.')} />
      ) : (
        <>
          <h1>{discussion.title}</h1>
          <p className="discussion-tags">
            <TagLinks tags={discussion.topicTags} />
          </p>
          <div className="form-actions">
            <button type="button" className="button-secondary" ref={editButton} onClick={() => setEditing(true)}>
              Edit<span className="visually-hidden"> discussion title and tags</span>
            </button>
            <button
              type="button"
              className="button-secondary button-secondary--danger"
              onClick={() => {
                setDeleteError(undefined);
                setDeleting(true);
              }}
            >
              Delete<span className="visually-hidden"> discussion</span>
            </button>
          </div>
        </>
      )}
      {deleting && (
        <ConfirmDialog
          title="Delete discussion"
          message={`Delete this discussion and all ${statementCount(discussion.statements.length)} in it, including the thesis? This cannot be undone.`}
          confirmLabel="Delete discussion"
          busyLabel="Deleting..."
          busy={deleteBusy}
          error={deleteError}
          onConfirm={() => void confirmDelete()}
          onCancel={() => setDeleting(false)}
        />
      )}
    </header>
  );
}

interface EditDiscussionFormProps {
  discussion: Discussion;
  onCancel: () => void;
  onSaved: () => void;
}

/** Edits the title and tags in place. Only changed fields are sent; API errors appear next to the field. */
function EditDiscussionForm({ discussion, onCancel, onSaved }: EditDiscussionFormProps) {
  const [title, setTitle] = useState(discussion.title);
  const [tags, setTags] = useState(formatTags(discussion.topicTags));
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const hasFieldErrors = error !== null && Object.keys(error.fieldErrors).length > 0;
  const otherFieldErrors = error
    ? Object.entries(error.fieldErrors)
        .filter(([field]) => field !== 'title' && field !== 'topicTags')
        .flatMap(([, messages]) => messages)
    : [];

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const topicTags = parseTags(tags);
    const changes = {
      ...(title !== discussion.title ? { title } : {}),
      ...(topicTags.join(' ') !== discussion.topicTags.join(' ') ? { topicTags } : {}),
    };
    if (Object.keys(changes).length === 0) {
      onCancel();
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      await updateDiscussion(discussion.id, changes);
      onSaved();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError(0, 'Something went wrong. Try again.'));
      setSubmitting(false);
    }
  }

  return (
    <form className="edit-form" aria-label="Edit discussion" onSubmit={handleSubmit} noValidate>
      <h1 className="visually-hidden">{discussion.title}</h1>
      {error && !hasFieldErrors && (
        <div role="alert" className="error-banner">
          <p>Could not save the discussion. {error.message}</p>
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
        id="edit-discussion-title"
        label="Title"
        value={title}
        onChange={setTitle}
        error={error?.fieldError('title')}
        hint="Up to 200 characters."
        disabled={submitting}
        autoFocus
      />
      <Field
        id="edit-discussion-tags"
        label="Topic tags"
        value={tags}
        onChange={setTags}
        error={error?.fieldError('topicTags')}
        hint="1 to 5 tags separated by commas or spaces, for example work.remote, management."
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
