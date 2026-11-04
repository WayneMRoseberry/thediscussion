import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import { ApiError, createDiscussion, listDiscussions } from '../api/client';
import type { DiscussionSummary } from '../api/types';
import { Field } from '../components/Field';

type ListState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'loaded'; discussions: DiscussionSummary[] };

export default function DiscussionListPage() {
  const [state, setState] = useState<ListState>({ status: 'loading' });

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const discussions = await listDiscussions(signal);
      setState({ status: 'loaded', discussions });
    } catch (error) {
      if (!signal?.aborted) {
        setState({ status: 'error', message: error instanceof Error ? error.message : 'Something went wrong.' });
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  return (
    <>
      <h1>Discussions</h1>

      {state.status === 'loading' && (
        <p role="status" className="loading">
          <span className="spinner" aria-hidden="true" />
          Loading discussions...
        </p>
      )}

      {state.status === 'error' && (
        <div role="alert" className="error-banner">
          <p>Could not load discussions. {state.message}</p>
          <button
            type="button"
            onClick={() => {
              setState({ status: 'loading' });
              void load();
            }}
          >
            Retry
          </button>
        </div>
      )}

      {state.status === 'loaded' && state.discussions.length === 0 && (
        <p className="empty-state">No discussions yet. Start the first one below.</p>
      )}

      {state.status === 'loaded' && state.discussions.length > 0 && (
        <ul className="discussion-list" aria-label="All discussions">
          {state.discussions.map((discussion) => (
            <li key={discussion.id}>
              <Link to={`/discussions/${discussion.id}`}>{discussion.title}</Link>{' '}
              <span className="topic-tag">
                <span className="visually-hidden">Topic: </span>
                {discussion.topicTag}
              </span>
            </li>
          ))}
        </ul>
      )}

      <CreateDiscussionForm />
    </>
  );
}

function CreateDiscussionForm() {
  const navigate = useNavigate();
  const [title, setTitle] = useState('');
  const [topicTag, setTopicTag] = useState('');
  const [thesis, setThesis] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      const created = await createDiscussion({ title, topicTag, thesis });
      navigate(`/discussions/${created.id}`);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError(0, 'Something went wrong. Try again.'));
      setSubmitting(false);
    }
  }

  const hasFieldErrors = error !== null && Object.keys(error.fieldErrors).length > 0;

  return (
    <section aria-labelledby="create-heading" className="create-discussion">
      <h2 id="create-heading">Start a discussion</h2>
      <form onSubmit={handleSubmit} noValidate aria-labelledby="create-heading">
        {error && !hasFieldErrors && (
          <p role="alert" className="form-error">
            {error.message}
          </p>
        )}
        <Field id="title" label="Title" value={title} onChange={setTitle} error={error?.fieldError('title')} hint="Up to 200 characters." />
        <Field
          id="topicTag"
          label="Topic tag"
          value={topicTag}
          onChange={setTopicTag}
          error={error?.fieldError('topicTag')}
          hint="Lowercase tags separated by dots, for example work.remote."
        />
        <Field
          id="thesis"
          label="Thesis"
          value={thesis}
          onChange={setThesis}
          error={error?.fieldError('thesis')}
          hint="The point you assert to be true. Up to 5000 characters."
          multiline
        />
        <div className="form-actions">
          <button type="submit" disabled={submitting}>
            {submitting ? 'Creating...' : 'Create discussion'}
          </button>
        </div>
      </form>
    </section>
  );
}
