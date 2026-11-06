import { Link } from 'react-router';
import type { DiscussionSummary } from '../api/types';
import { TagLinks } from './TagLinks';

/** Discussions as links, each followed by its tags (which link to their tag pages). */
export function DiscussionList({ discussions, label }: { discussions: DiscussionSummary[]; label: string }) {
  return (
    <ul className="discussion-list" aria-label={label}>
      {discussions.map((discussion) => (
        <li key={discussion.id}>
          <Link to={`/discussions/${discussion.id}`} className="discussion-link">
            {discussion.title}
          </Link>{' '}
          <TagLinks tags={discussion.topicTags} />
        </li>
      ))}
    </ul>
  );
}
