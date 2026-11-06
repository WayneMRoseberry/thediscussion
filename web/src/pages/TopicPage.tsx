import { useCallback } from 'react';
import { Link, useParams } from 'react-router';
import { ApiError, getTopics, listDiscussionsUnderTag } from '../api/client';
import type { DiscussionSummary, TopicNode } from '../api/types';
import { DiscussionList } from '../components/DiscussionList';
import { TopicCount } from '../components/TopicTree';
import { isValidTag, tagAncestry, tagName, tagPath } from '../tags/tags';
import { errorMessage, useLoad } from './useLoad';

/** /topics/{tag}: breadcrumbs for each segment, the tag's sub-tags with counts, and the discussions at or under it. */
export default function TopicPage() {
  const { tag = '' } = useParams();

  return isValidTag(tag) ? <TopicContent key={tag} tag={tag} /> : <InvalidTopic tag={tag} />;
}

interface TopicData {
  /** The tag's node in the tree; undefined when no discussion uses it (tags exist only through discussions). */
  node?: TopicNode;
  discussions: DiscussionSummary[];
}

function TopicContent({ tag }: { tag: string }) {
  const load = useCallback(
    async (signal: AbortSignal): Promise<TopicData> => {
      const [topics, discussions] = await Promise.all([getTopics(signal), listDiscussionsUnderTag(tag, signal)]);
      return { node: findTopic(topics, tag), discussions };
    },
    [tag],
  );
  const { state, retry } = useLoad(load);

  if (state.status === 'error' && state.error instanceof ApiError && state.error.status === 400) {
    return <InvalidTopic tag={tag} />;
  }

  return (
    <>
      <TopicBreadcrumb tag={tag} />
      <h1>
        <span className="visually-hidden">Topic: </span>
        {tag}
      </h1>

      {state.status === 'loading' && (
        <p role="status" className="loading">
          <span className="spinner" aria-hidden="true" />
          Loading discussions under {tag}...
        </p>
      )}

      {state.status === 'error' && (
        <div role="alert" className="error-banner">
          <p>Could not load this topic. {errorMessage(state.error)}</p>
          <button type="button" onClick={retry}>
            Retry
          </button>
        </div>
      )}

      {state.status === 'loaded' && (
        <>
          {state.value.node && state.value.node.children.length > 0 && (
            <section aria-labelledby="subtopics-heading" className="subtopics">
              <h2 id="subtopics-heading">Sub-topics</h2>
              <ul className="subtopic-list">
                {state.value.node.children.map((child) => (
                  <li key={child.tag}>
                    <Link to={tagPath(child.tag)}>{child.name}</Link> <TopicCount count={child.discussionCount} />
                  </li>
                ))}
              </ul>
            </section>
          )}

          {state.value.discussions.length === 0 ? (
            <p className="empty-state">No discussions under {tag}</p>
          ) : (
            <section aria-labelledby="topic-discussions-heading">
              <h2 id="topic-discussions-heading">
                {state.value.discussions.length} {state.value.discussions.length === 1 ? 'discussion' : 'discussions'}
              </h2>
              <DiscussionList discussions={state.value.discussions} label={`Discussions under ${tag}`} />
            </section>
          )}
        </>
      )}
    </>
  );
}

function InvalidTopic({ tag }: { tag: string }) {
  return (
    <>
      <nav aria-label="Breadcrumb" className="breadcrumb">
        <ol>
          <li>
            <Link to="/topics">Topics</Link>
          </li>
          <li aria-current="page">Invalid topic</li>
        </ol>
      </nav>
      <h1>Invalid topic</h1>
      <p>
        &ldquo;{tag}&rdquo; is not a valid topic tag. Tags are 1 to 5 dot-separated segments of lowercase letters, digits
        or hyphens, for example work.remote. <Link to="/topics">Browse all topics</Link>
      </p>
    </>
  );
}

/** Topics &gt; work &gt; remote: every segment above the current one links to its own tag page. */
function TopicBreadcrumb({ tag }: { tag: string }) {
  const ancestry = tagAncestry(tag);
  return (
    <nav aria-label="Breadcrumb" className="breadcrumb">
      <ol>
        <li>
          <Link to="/topics">Topics</Link>
        </li>
        {ancestry.map((ancestor, index) =>
          index === ancestry.length - 1 ? (
            <li key={ancestor} aria-current="page">
              {tagName(ancestor)}
            </li>
          ) : (
            <li key={ancestor}>
              <Link to={tagPath(ancestor)}>{tagName(ancestor)}</Link>
            </li>
          ),
        )}
      </ol>
    </nav>
  );
}

function findTopic(topics: TopicNode[], tag: string): TopicNode | undefined {
  for (const topic of topics) {
    if (topic.tag === tag) return topic;
    if (tag.startsWith(`${topic.tag}.`)) return findTopic(topic.children, tag);
  }
  return undefined;
}
