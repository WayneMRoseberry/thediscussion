import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router';
import { ApiError, getDiscussion } from '../api/client';
import type { Discussion } from '../api/types';
import { AddStatementPanel } from '../components/AddStatementPanel';
import { DiscussionHeader } from '../components/DiscussionHeader';
import { Outline } from '../components/Outline';

type LoadState =
  | { status: 'loading' }
  | { status: 'notFound' }
  | { status: 'error'; message: string }
  | { status: 'loaded'; discussion: Discussion };

/** Loads one discussion. `reload` refetches without blanking what is already shown. */
function useDiscussion(id: string) {
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const latestRequest = useRef(0);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      const request = ++latestRequest.current;
      try {
        const discussion = await getDiscussion(id, signal);
        if (request === latestRequest.current) {
          setState({ status: 'loaded', discussion });
        }
      } catch (error) {
        if (signal?.aborted || request !== latestRequest.current) {
          return;
        }
        if (error instanceof ApiError && error.isNotFound) {
          setState({ status: 'notFound' });
        } else {
          setState({ status: 'error', message: error instanceof Error ? error.message : 'Something went wrong.' });
        }
      }
    },
    [id],
  );

  useEffect(() => {
    const controller = new AbortController();
    setState({ status: 'loading' });
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const retry = useCallback(() => {
    setState({ status: 'loading' });
    void load();
  }, [load]);

  const reload = useCallback(() => void load(), [load]);

  return { state, retry, reload };
}

export default function DiscussionPage() {
  const { id = '' } = useParams();
  const { state, retry, reload } = useDiscussion(id);
  const [announcement, setAnnouncement] = useState('');

  // After an add, edit or delete: announce it and reload the outline and statuses.
  const changed = useCallback(
    (message: string) => {
      setAnnouncement(message);
      reload();
    },
    [reload],
  );

  return (
    <>
      <nav aria-label="Breadcrumb" className="breadcrumb">
        <ol>
          <li>
            <Link to="/">Discussions</Link>
          </li>
          <li aria-current="page">{state.status === 'loaded' ? state.discussion.title : 'Discussion'}</li>
        </ol>
      </nav>

      {state.status === 'loading' && (
        <p role="status" className="loading">
          <span className="spinner" aria-hidden="true" />
          Loading discussion...
        </p>
      )}

      {state.status === 'notFound' && (
        <>
          <h1>Discussion not found</h1>
          <p>
            There is no discussion at this address. <Link to="/">Back to all discussions</Link>
          </p>
        </>
      )}

      {state.status === 'error' && (
        <>
          <h1>Discussion</h1>
          <div role="alert" className="error-banner">
            <p>Could not load the discussion. {state.message}</p>
            <button type="button" onClick={retry}>
              Retry
            </button>
          </div>
        </>
      )}

      {state.status === 'loaded' && (
        <>
          <DiscussionHeader discussion={state.discussion} onChanged={changed} />
          <p role="status" className="announcement">
            {announcement}
          </p>
          <section aria-labelledby="argument-heading">
            <h2 id="argument-heading" className="visually-hidden">
              Argument
            </h2>
            <Outline discussion={state.discussion} onChanged={changed} />
          </section>
          <AddStatementPanel discussion={state.discussion} onAdded={changed} />
        </>
      )}
    </>
  );
}
