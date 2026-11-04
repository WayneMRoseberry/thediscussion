import type { Discussion, ThesisStatus } from '../api/types';
import { buildOutline, groupTypeLabels, kindLabels, markerFor, type OutlineNode } from '../outline/outline';

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

/** The discussion as an indented outline: thesis, support groups, statements and their challenges. */
export function Outline({ discussion }: { discussion: Discussion }) {
  const root = buildOutline(discussion.statements, discussion.thesisId);
  if (!root) {
    return <p>This discussion has no thesis.</p>;
  }

  return (
    <ul className="outline" aria-label="Argument outline">
      <OutlineItem node={root} status={discussion.status} />
    </ul>
  );
}

function OutlineItem({ node, status }: { node: OutlineNode; status: ThesisStatus }) {
  const { statement } = node;
  const marker = markerFor(statement);
  const glyph = statement.kind === 'counterArgument' || statement.kind === 'rebuttal' ? 'x' : statement.kind === 'supportGroup' ? '+' : null;

  return (
    <li className={`outline-item outline-item--${statement.kind}`} data-statement-id={statement.id}>
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
      {node.children.length > 0 && (
        <ul>
          {node.children.map((child) => (
            <OutlineItem key={child.statement.id} node={child} status={status} />
          ))}
        </ul>
      )}
    </li>
  );
}
