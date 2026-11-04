import { Link } from 'react-router';
import type { TopicNode } from '../api/types';
import { tagPath } from '../tags/tags';

/** "(4)" on screen, "(4 discussions)" for screen readers. */
export function TopicCount({ count }: { count: number }) {
  return (
    <span className="topic-count">
      ({count}
      <span className="visually-hidden"> {count === 1 ? 'discussion' : 'discussions'}</span>)
    </span>
  );
}

/** The tag hierarchy as nested lists: each tag links to its page and shows how many discussions are at or under it. */
export function TopicTree({ topics, label }: { topics: TopicNode[]; label?: string }) {
  return (
    <ul className="topic-tree" aria-label={label}>
      {topics.map((topic) => (
        <li key={topic.tag}>
          <Link to={tagPath(topic.tag)}>{topic.tag}</Link> <TopicCount count={topic.discussionCount} />
          {topic.children.length > 0 && <TopicTree topics={topic.children} />}
        </li>
      ))}
    </ul>
  );
}
