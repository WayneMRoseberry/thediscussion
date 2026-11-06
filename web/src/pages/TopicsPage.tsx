import { Link } from 'react-router';
import { getTopics } from '../api/client';
import { TopicTree } from '../components/TopicTree';
import { errorMessage, useLoad } from './useLoad';

/** /topics: every tag in use, as a tree with discussion counts. */
export default function TopicsPage() {
  const { state, retry } = useLoad(getTopics);

  return (
    <>
      <nav aria-label="Breadcrumb" className="breadcrumb">
        <ol>
          <li>
            <Link to="/">Discussions</Link>
          </li>
          <li aria-current="page">Topics</li>
        </ol>
      </nav>
      <h1>Topics</h1>

      {state.status === 'loading' && (
        <p role="status" className="loading">
          <span className="spinner" aria-hidden="true" />
          Loading topics...
        </p>
      )}

      {state.status === 'error' && (
        <div role="alert" className="error-banner">
          <p>Could not load topics. {errorMessage(state.error)}</p>
          <button type="button" onClick={retry}>
            Retry
          </button>
        </div>
      )}

      {state.status === 'loaded' && state.value.length === 0 && (
        <p className="empty-state">
          No topics yet. Topics appear when a discussion is tagged. <Link to="/">Start a discussion</Link>
        </p>
      )}

      {state.status === 'loaded' && state.value.length > 0 && <TopicTree topics={state.value} label="All topics" />}
    </>
  );
}
