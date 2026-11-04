// Wire types for the REST API (/api/v1). Keep in sync with src/TheDiscussion.Api/Discussions/DiscussionContracts.cs.

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
  topicTag: string;
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
  createdAt: string;
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
  topicTag: string;
  thesis: string;
}

export interface AddStatementInput {
  kind: AddableKind;
  parentId: string;
  text?: string;
  groupType?: SupportGroupType;
}

/** RFC 7807 problem details, with ASP.NET Core's validation `errors` extension. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}
