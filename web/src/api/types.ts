// Wire types for the REST API (/api/v1). Keep in sync with src/TheDiscussion.Api/Discussions/DiscussionContracts.cs
// and src/TheDiscussion.Api/Topics/TopicEndpoints.cs.

export type ThesisStatus = 'unsupported' | 'open' | 'countered' | 'tested';

export type StatementKind =
  | 'thesis'
  | 'supportGroup'
  | 'supportingStatement'
  | 'illustration'
  | 'counterArgument'
  | 'rebuttal';

/** Kinds a client may add (the thesis is created with the discussion). */
export type AddableKind = Exclude<StatementKind, 'thesis'>;

export type SupportGroupType = 'allRequired' | 'anySufficient';

export interface DiscussionSummary {
  id: string;
  title: string;
  /** 1 to 5 distinct tags, in the order they were given. */
  topicTags: string[];
  createdAt: string;
}

export interface Statement {
  id: string;
  kind: StatementKind;
  parentId: string | null;
  /** Absent on support groups. */
  text?: string;
  /** Present on support groups. */
  groupType?: SupportGroupType;
  /**
   * Present on support groups (the API leaves it out for every other kind): 1 to 100 characters, or null for a group
   * stored before groups had names.
   */
  name?: string | null;
  createdAt: string;
  /** When the text (or a group's name or type) was last edited; null until the first edit. */
  editedAt: string | null;
  /** Present on supporting statements. */
  stands?: boolean;
  /** Present on counter arguments and rebuttals. */
  refuted?: boolean;
  /** Present on non-empty support groups. */
  holds?: boolean;
}

export interface Discussion extends DiscussionSummary {
  status: ThesisStatus;
  thesisId: string;
  /** Flat list in creation order; the tree is given by parentId. */
  statements: Statement[];
}

export interface CreateDiscussionInput {
  title: string;
  topicTags: string[];
  thesis: string;
}

/** Only the fields sent are changed. */
export interface UpdateDiscussionInput {
  title?: string;
  topicTags?: string[];
}

export interface AddStatementInput {
  kind: AddableKind;
  parentId: string;
  text?: string;
  /** Support groups only: required by the API, 1 to 100 characters after trimming. */
  name?: string;
  groupType?: SupportGroupType;
}

/** PATCH body for a support group: only the fields sent change, and a group never takes text. */
export interface UpdateGroupInput {
  name?: string;
  groupType?: SupportGroupType;
}

/** A node of GET /api/v1/topics. */
export interface TopicNode {
  tag: string;
  /** The tag's last segment. */
  name: string;
  /** Distinct discussions tagged with this tag or one of its sub-tags. */
  discussionCount: number;
  /** Direct sub-tags, sorted by name. */
  children: TopicNode[];
}

/** RFC 7807 problem details, with ASP.NET Core's validation `errors` extension. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}
