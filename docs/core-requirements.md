# Product Vision
The Discussion is a discussion forum which hosts structured debates. Topics are presented in a structured manner that makes the nature of a point and its supporting arguments clear and formally described. Counter arguments and rebuttals are presented in a formal and structured manner as well. This allows for a status on an argument, whether a point is settled, open, supported, or rebutted.

## Structured Arguments
An argument starts with a thesis, which is the point that one asserts to be true. A thesis has supporting statements which must also be true for the thesis to be true. Supporting statements have different relationships to each other based on whether one or more are sufficient, or must go together, and whether eliminating one or all of them are sufficient to refute the thesis.

### Structure Extraction
The discussion allows a user to enter an argument as free form text, non-structured in multiple paragraph form. After submission, structure will be inferred, extracted into a thesis and supporting statements.

## Counter Arguments
Counter arguments are made against a thesis, or any point supporting a thesis.

## Illustrative Versus Argumentative
Some statements illustrate a point rather than support it. Illustrative statements are meant to make something easier to understand. Countering an illustration does not necessarily counter the point it illustrates.

## Logical Fallacy Tagging
Logical fallacies are offered as tags that one can assert against one or more statements. Logical fallacies require an explanation justifying their application.

### Automatic Logical Fallacy Detection
The Discussion offers automatic logical fallacy detection. This shows for authors as they write their statements, giving them a chance to consider their arguments before submission. The feature is also available to people considering a counter argument.

## Argument Status
Argument status is determined automatically based whether its thesis and support points have been tested and rebutted.
An argument's status is one of the following:

### Unsupported
A thesis has been put forward, but is insufficiently supported to stand.

### Open
A thesis has been put forward with supporing arguments, but has not received any counter arguments to test its validity.

### Countered
A counter argument has been offered that remains unrebutted or refuted.

### Tested
A thesis has been supported, tested with counter arguments, all of which have been refuted.

## Participants
Users participate in discussions and "own" their points.

### Roles
A user may have one or more roles. Roles have distinct names. Roles are transparently associated with the user they belong to. When a user posts a statement, their name and role are indicated as the one which posted that statement.

### Role to Statement Affinity-Side
Once a user posts a statement with a given taking a side in a discussion, that role remains on that side. They do not switch sides. That user may offer a counter argument, but they must switch roles to do so.

### Sign-up
A user signs up for The Discussion using an email and password combination. Sign-up uses common web-based application means for establishing a person's actual identity, requiring the user to respond to an email as given to confirm they are indeed that user.

A user may cancel their account at any time. A user may change their password at any time.

### User Discussion Purge
A user may instruct The Discussion to delete any or all of their dicussion statements. This may be done one at a time, or in bulk. When a user deletes the "root" of a set of statements, all statements from that root are likewise deleted, as are any statements from other users associated with it.
A user may instruct The Discussion to delete all statements they have made. This option is offered on account de-activation.

Admin accounts have the ability to remove all statements by a given user even when a given user account has been de-activated. This is to cover the case when a user de-activates their account but does not ask for removal, and then later some motivating reason makes removal necessary.

## User Experience
The homepage experience presents the following options:
- Discussion topics
- My Discussions
- Hot Topics
- Discussion Trends and Stats
- A "How to discuss" Learning Space

### Sub-Page Topics
#### Discussion Topics
Discussions are categorized based on discussion topics. Topics are hierarchical tags in a "tag.subtag" format. The tag structure allows for navigation of different discussions.

#### My Discussions
My Discussions presents the discussions that the logged in user has participated in, or discussions where they have been mentioned or notified about.

#### Hot Topics
Hot topics includes topic categories getting the most updates on discussions in them, newest topics catories, newest discussions, and discussions getting the most likes or down votes.

#### Discussion Trends and Stats
Trends and stats give numbers on which topics are engaging at what volume, movement of topics through different status, user roles making most points, and more.

#### How to Discuss Learning Spaces
Partly instructions on how to use the site. Partly lessons on ethics and etiquette of discussion. Partly lessons on logical argument types, logical fallacy types, and how to frame or imagine an argument. The point of this section is to teach people how to use argumentation as a means of learning, discovery, and reasoning.

Arguing to win at the cost of reason is consider an non-ethical practice in The Discussion. The forum may clear specific spaces where people do that for the fun of doing so, but those spaces will be clearly delineated to avoid confusion. Ethical, responsible, respectful rules of engagement are expected and the point of this forum is to teach that.

### Discussion Presentation
Structured discussions are visually discernable. A main point and its supporting points are arranged so that their logical relationship is easy to see. Refutations are similarly aligned so that how they disrupt an argument is clear.

Different modes may be necessary to help different visualizations. Argument structure will be more visually oriented, shortening text for sake of the visual. Actually reading the text of an argument would be less about visual structure, even if there will need to be layout conventions used to make relationships within the argument clear.

# Architecture

## Front/Application/Storage
The Discussion will use a three tiered architecture. The front end will be web-based. Business logic will happen in the application tier, which exposes itself to the front end via a REST API. The same REST API will allow public access, requiring accounts with private API keys. The application tier will communicate with the storage tier to retriev and persist all information about users and discussions. The storage tier will be available only to the application tier.

## Storage Tier Pluggability and Extensibility
The storage tier shall be built to be replaceable. The application tier shall be designed to be storage technology independent.

## Deployment
The product shall utilize a CI/CD pipeline that supports the following:
- automatic deployment
- automated checks in the CI/CD pipeline
- blue/green checks at deployment time based on synthetic monitors on new revision
- prior version rollback
- feature flags that can be flipped without a code change
- Deployable to ad hoc test environment when needed
- Main is always deployable. Every change that is not gated by failure deploys.

## Monitoring and Logging
The system shall utilize logging that allows monitoring of application behavior and customer usage. Synthetic monitors shall report on issues and availability.

## Load and Scale
First version should anticipate very little usage. Build for multiple user access. Build to anticipate scale even if MCP does not achieve it. Delineate difference in scale and performance plans for MCP versus future versions.

## Build For Safe Deployment Over Completed Stories
The rule for submitting code is submit once the code is safe and feedback is possible. Some tenets on this:
- testing behind a feature flag is a feedback
- code being in trunk that others can sycn to is feedback
- regression suite results is feedback
- monitoring signals from features that are user "invisible" is feedback
- smallest safe change possible is lower risk than lots of change all at once
- completed integration is high risk, stabilize and deploy small changes first
- organize code change schedule to maximize observability and testability