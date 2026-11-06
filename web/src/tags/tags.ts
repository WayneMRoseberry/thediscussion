// Topic tags: 1 to 5 dot-separated lowercase segments of [a-z0-9-], for example "work.remote".
// Mirrors DiscussionRules on the server, which stays the source of truth for validation.

const tagPattern = /^[a-z0-9-]+(\.[a-z0-9-]+){0,4}$/;

/** Splits what a user typed into tags. Tags are separated by commas or spaces; empty entries are dropped. */
export function parseTags(input: string): string[] {
  return input.split(/[\s,]+/).filter((tag) => tag.length > 0);
}

/** The text a tag input shows for a list of tags. */
export function formatTags(tags: string[]): string {
  return tags.join(', ');
}

export function isValidTag(tag: string): boolean {
  return tagPattern.test(tag);
}

/** The tag of each segment from the root down: "work.remote.tools" gives ["work", "work.remote", "work.remote.tools"]. */
export function tagAncestry(tag: string): string[] {
  const segments = tag.split('.');
  return segments.map((_, index) => segments.slice(0, index + 1).join('.'));
}

/** The last segment of a tag: "work.remote" gives "remote". */
export function tagName(tag: string): string {
  return tag.slice(tag.lastIndexOf('.') + 1);
}

export function tagPath(tag: string): string {
  return `/topics/${encodeURIComponent(tag)}`;
}
