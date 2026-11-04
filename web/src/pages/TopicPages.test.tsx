import { screen, within } from '@testing-library/react';
import { axe } from 'vitest-axe';
import type { DiscussionSummary, TopicNode } from '../api/types';
import { mockApi, networkFailure, validationProblem } from '../test/fakeApi';
import { renderAt } from '../test/render';

function node(tag: string, discussionCount: number, ...children: TopicNode[]): TopicNode {
  return { tag, name: tag.slice(tag.lastIndexOf('.') + 1), discussionCount, children };
}

/** The topic navigation reference. */
const referenceTopics: TopicNode[] = [
  node('science', 3, node('science.climate', 3)),
  node('work', 7, node('work.hours', 2), node('work.remote', 4, node('work.remote.tools', 1))),
];

const remoteDiscussions: DiscussionSummary[] = [
  { id: 'd1', title: 'Remote work boosts productivity', topicTags: ['work.remote', 'management'], createdAt: '2026-11-03T12:00:00Z' },
  { id: 'd2', title: 'Offices are obsolete', topicTags: ['work.remote'], createdAt: '2026-11-02T12:00:00Z' },
  { id: 'd3', title: 'Remote tooling is immature', topicTags: ['work.remote.tools'], createdAt: '2026-11-01T12:00:00Z' },
  { id: 'd4', title: 'Async beats meetings', topicTags: ['work.remote', 'work.hours'], createdAt: '2026-10-31T12:00:00Z' },
];

/** Serves the reference tree and the discussions under each tag. */
function serveReference() {
  return mockApi(({ path }) => {
    if (path === '/api/v1/topics') return { status: 200, body: referenceTopics };
    const tag = new URL(path, 'http://localhost').searchParams.get('tag');
    if (tag === 'work.remote') return { status: 200, body: remoteDiscussions };
    if (tag === 'work.remote.tools') return { status: 200, body: [remoteDiscussions[2]] };
    if (tag === 'work') return { status: 200, body: [...remoteDiscussions, { id: 'd5', title: 'Four-day weeks', topicTags: ['work.hours'], createdAt: '' }] };
    return { status: 200, body: [] };
  });
}

/** The text of a tree entry without its nested sub-topics. */
function entryText(link: HTMLElement): string {
  const item = link.closest('li')!;
  return Array.from(item.childNodes)
    .filter((child) => !(child instanceof HTMLElement && child.tagName === 'UL'))
    .map((child) => child.textContent)
    .join('')
    .replace(/\s+/g, ' ')
    .trim();
}

describe('Topics page', () => {
  it('shows a loading state', () => {
    mockApi(() => new Promise(() => {}));

    renderAt('/topics');

    expect(screen.getByRole('status')).toHaveTextContent('Loading topics');
  });

  it('shows the tag tree with counts, each tag linking to its page', async () => {
    serveReference();

    renderAt('/topics');

    const tree = await screen.findByRole('list', { name: 'All topics' });
    expect(screen.getByRole('heading', { level: 1, name: 'Topics' })).toBeInTheDocument();
    const topLevel = Array.from(tree.querySelectorAll(':scope > li > a')).map((link) => link.textContent);
    expect(topLevel).toEqual(['science', 'work']);
    const work = within(tree).getByRole('link', { name: 'work' });
    expect(entryText(work)).toBe('work (7 discussions)');
    expect(work).toHaveAttribute('href', '/topics/work');
    const workItem = work.closest('li')!;
    const workChildren = Array.from(workItem.querySelectorAll(':scope > ul > li > a')).map((link) => entryText(link as HTMLElement));
    expect(workChildren).toEqual(['work.hours (2 discussions)', 'work.remote (4 discussions)']);
    const tools = within(workItem).getByRole('link', { name: 'work.remote.tools' });
    expect(entryText(tools)).toBe('work.remote.tools (1 discussion)');
    expect(tools.closest('ul')!.closest('li')!.querySelector(':scope > a')).toHaveTextContent('work.remote');
    expect(within(tree).getByRole('link', { name: 'science.climate' })).toHaveAttribute('href', '/topics/science.climate');
  });

  it('shows an empty state when no discussion is tagged yet', async () => {
    mockApi(() => ({ status: 200, body: [] }));

    renderAt('/topics');

    expect(await screen.findByText(/No topics yet/)).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'All topics' })).not.toBeInTheDocument();
  });

  it('shows an error with a retry that reloads', async () => {
    let calls = 0;
    mockApi(() => {
      calls += 1;
      return calls === 1 ? networkFailure() : { status: 200, body: referenceTopics };
    });
    const { user } = renderAt('/topics');

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Could not load topics');
    await user.click(within(alert).getByRole('button', { name: 'Retry' }));

    expect(await screen.findByRole('list', { name: 'All topics' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('navigates from a tag in the tree to its page', async () => {
    serveReference();
    const { user } = renderAt('/topics');

    await user.click(await screen.findByRole('link', { name: 'work.remote' }));

    expect(await screen.findByRole('heading', { level: 2, name: '4 discussions' })).toBeInTheDocument();
  });

  it('has no accessibility violations with the tree, empty and error states', async () => {
    serveReference();
    const loaded = renderAt('/topics');
    await screen.findByRole('list', { name: 'All topics' });
    expect(await axe(loaded.container)).toHaveNoViolations();
    loaded.unmount();

    mockApi(() => ({ status: 200, body: [] }));
    const empty = renderAt('/topics');
    await screen.findByText(/No topics yet/);
    expect(await axe(empty.container)).toHaveNoViolations();
    empty.unmount();

    mockApi(() => ({ status: 500, body: { title: 'Server error', status: 500 } }));
    const failed = renderAt('/topics');
    await screen.findByRole('alert');
    expect(await axe(failed.container)).toHaveNoViolations();
  });
});

describe('Tag page', () => {
  it('shows a loading state', () => {
    mockApi(() => new Promise(() => {}));

    renderAt('/topics/work.remote');

    expect(screen.getByRole('status')).toHaveTextContent('Loading discussions under work.remote');
  });

  it('shows breadcrumbs for each segment, the sub-topics with counts and the matching discussions with their tags', async () => {
    const fetchMock = serveReference();

    renderAt('/topics/work.remote');

    expect(await screen.findByRole('heading', { level: 2, name: '4 discussions' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('work.remote');

    const breadcrumb = screen.getByRole('navigation', { name: 'Breadcrumb' });
    const crumbs = within(breadcrumb).getAllByRole('listitem');
    expect(crumbs.map((crumb) => crumb.textContent)).toEqual(['Topics', 'work', 'remote']);
    expect(within(breadcrumb).getByRole('link', { name: 'Topics' })).toHaveAttribute('href', '/topics');
    expect(within(breadcrumb).getByRole('link', { name: 'work' })).toHaveAttribute('href', '/topics/work');
    expect(crumbs[2]).toHaveAttribute('aria-current', 'page');
    expect(within(crumbs[2]).queryByRole('link')).not.toBeInTheDocument();

    const subtopics = screen.getByRole('region', { name: 'Sub-topics' });
    const tools = within(subtopics).getByRole('link', { name: 'tools' });
    expect(tools).toHaveAttribute('href', '/topics/work.remote.tools');
    expect(tools.closest('li')).toHaveTextContent('tools (1 discussion)');

    const list = screen.getByRole('list', { name: 'Discussions under work.remote' });
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(4);
    expect(within(items[0]).getByRole('link', { name: 'Remote work boosts productivity' })).toHaveAttribute('href', '/discussions/d1');
    expect(within(items[0]).getAllByRole('link').slice(1).map((link) => link.textContent)).toEqual(['#work.remote', '#management']);
    expect(within(items[3]).getByRole('link', { name: '#work.hours' })).toHaveAttribute('href', '/topics/work.hours');
    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual(expect.arrayContaining(['/api/v1/topics', '/api/v1/discussions?tag=work.remote']));
  });

  it('says "1 discussion" and shows no sub-topics for a leaf tag', async () => {
    serveReference();

    renderAt('/topics/work.remote.tools');

    expect(await screen.findByRole('heading', { level: 2, name: '1 discussion' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Sub-topics' })).not.toBeInTheDocument();
  });

  it('shows the empty state for a tag no discussion uses', async () => {
    serveReference();

    renderAt('/topics/work.remote.unknown');

    expect(await screen.findByText('No discussions under work.remote.unknown')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Sub-topics' })).not.toBeInTheDocument();
    expect(within(screen.getByRole('navigation', { name: 'Breadcrumb' })).getByRole('link', { name: 'remote' })).toHaveAttribute(
      'href',
      '/topics/work.remote',
    );
  });

  it('shows the invalid-tag state without calling the API', async () => {
    const fetchMock = mockApi(() => ({ status: 200, body: [] }));

    renderAt('/topics/Work..Remote');

    expect(screen.getByRole('heading', { level: 1, name: 'Invalid topic' })).toBeInTheDocument();
    expect(screen.getByText(/is not a valid topic tag/)).toHaveTextContent('“Work..Remote” is not a valid topic tag.');
    expect(screen.getByRole('link', { name: 'Browse all topics' })).toHaveAttribute('href', '/topics');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('shows the invalid-tag state when the API rejects the tag', async () => {
    mockApi(({ path }) => (path === '/api/v1/topics' ? { status: 200, body: [] } : validationProblem({ tag: ['Tag must be ...'] })));

    renderAt('/topics/work');

    expect(await screen.findByRole('heading', { level: 1, name: 'Invalid topic' })).toBeInTheDocument();
  });

  it('shows an error with a retry that reloads', async () => {
    let failed = false;
    mockApi(({ path }) => {
      if (!failed && path.startsWith('/api/v1/discussions')) {
        failed = true;
        return { status: 500, body: { title: 'Server error', status: 500 } };
      }
      return path === '/api/v1/topics' ? { status: 200, body: referenceTopics } : { status: 200, body: remoteDiscussions };
    });
    const { user } = renderAt('/topics/work.remote');

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Could not load this topic. Server error');
    await user.click(within(alert).getByRole('button', { name: 'Retry' }));

    expect(await screen.findByRole('heading', { level: 2, name: '4 discussions' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('moves up and down the hierarchy through breadcrumbs and sub-topics', async () => {
    serveReference();
    const { user } = renderAt('/topics/work.remote');
    await screen.findByRole('heading', { level: 2, name: '4 discussions' });

    await user.click(within(screen.getByRole('navigation', { name: 'Breadcrumb' })).getByRole('link', { name: 'work' }));
    expect(await screen.findByRole('heading', { level: 2, name: '5 discussions' })).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Sub-topics' })).getAllByRole('link').map((link) => link.textContent)).toEqual([
      'hours',
      'remote',
    ]);

    await user.click(within(screen.getByRole('region', { name: 'Sub-topics' })).getByRole('link', { name: 'remote' }));
    expect(await screen.findByRole('heading', { level: 2, name: '4 discussions' })).toBeInTheDocument();

    await user.click(within(screen.getByRole('region', { name: 'Sub-topics' })).getByRole('link', { name: 'tools' }));
    expect(await screen.findByRole('heading', { level: 2, name: '1 discussion' })).toBeInTheDocument();

    await user.click(within(screen.getByRole('navigation', { name: 'Breadcrumb' })).getByRole('link', { name: 'Topics' }));
    expect(await screen.findByRole('list', { name: 'All topics' })).toBeInTheDocument();
  });

  it('has no accessibility violations in the loaded, empty, invalid and error states', async () => {
    serveReference();
    const loaded = renderAt('/topics/work.remote');
    await screen.findByRole('heading', { level: 2, name: '4 discussions' });
    expect(await axe(loaded.container)).toHaveNoViolations();
    loaded.unmount();

    serveReference();
    const empty = renderAt('/topics/nothing.here');
    await screen.findByText('No discussions under nothing.here');
    expect(await axe(empty.container)).toHaveNoViolations();
    empty.unmount();

    const invalid = renderAt('/topics/NOT_A_TAG');
    expect(await axe(invalid.container)).toHaveNoViolations();
    invalid.unmount();

    mockApi(() => networkFailure());
    const failed = renderAt('/topics/work');
    await screen.findByRole('alert');
    expect(await axe(failed.container)).toHaveNoViolations();
  });
});
