import { Link } from 'react-router';
import { tagPath } from '../tags/tags';

/** A discussion's topic tags, each linking to its tag page (#work.remote). */
export function TagLinks({ tags }: { tags: string[] }) {
  return (
    <span className="topic-tags">
      <span className="visually-hidden">Topics: </span>
      {tags.map((tag, index) => (
        <span key={tag}>
          {index > 0 && ' '}
          <Link to={tagPath(tag)} className="topic-tag">
            #{tag}
          </Link>
        </span>
      ))}
    </span>
  );
}
