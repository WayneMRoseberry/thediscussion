# Manual check AC-6: nested arguments read clearly in the outline

| | |
|---|---|
| Plan | `.zeroshot/plans/nested-named-groups.md`, AC-6 (`Check (manual)`) |
| Test design | `docs/test-plans/nested-named-groups-use-cases.md`, UC-6 and the Cluster B variables "Reviewer", "UI nesting shown" and "Migrated null-name group" |
| Who | Wayne, in a real browser, before the pull request is merged |
| Result | Sign-off, or recorded findings. Confusing layout or wrong statuses block the merge (UC-6 E2) |

The automated tests cover rendering, validation and status rules (AC-1 to AC-5). This check covers what they
cannot: does a nested argument *read* clearly to a person?

## 1. Start the app

Commands are from `README.md`. The API listens on <http://localhost:5080> and the web app on <http://localhost:5173>.

In-memory storage (sections 2 to 5; data is lost when the API stops):

```sh
dotnet run --project src/TheDiscussion.Api --launch-profile InMemory
```

PostgreSQL storage (needed for section 6, the unnamed group; migrations are applied at startup):

```powershell
# PowerShell
$env:ConnectionStrings__TheDiscussion = "Host=localhost;Port=5432;Database=thediscussion;Username=thediscussion;Password=<password>"
dotnet run --project src/TheDiscussion.Api --launch-profile Postgres
```

```sh
# bash
export ConnectionStrings__TheDiscussion="Host=localhost;Port=5432;Database=thediscussion;Username=thediscussion;Password=<password>"
dotnet run --project src/TheDiscussion.Api --launch-profile Postgres
```

Web front end, in a second terminal:

```sh
cd web
npm ci
npm run dev
```

## 2. Seed the discussion in the browser

Short names used below: **G1** to **G4** are groups, **S1** to **S5** supporting statements, **C1** to **C3**
counter arguments, **R1** and **R2** rebuttals.

On the Discussions page, fill in *Start a discussion* and select **Create discussion**. Then use the
*Add to the discussion* buttons. The first column of the table names the button, the picker label
(**Supports**, **Support group**, **Counters**, **Rebuts**) and the picker option to choose.

| # | Button, picker option | Name / text, group type |
|---|---|---|
| 1 | *Start a discussion* | Title `Remote work and productivity`, topic tags `work.remote`, thesis `Remote work boosts productivity` |
| 2 | **+ Add support group**, Supports `[THESIS] Remote work boosts productivity` | G1 `Productivity mechanisms`, All required |
| 3 | **+ Add support**, Support group `ALL REQUIRED: Productivity mechanisms` | S1 `Fewer interruptions` |
| 4 | **+ Add support group**, Supports `ALL REQUIRED: Productivity mechanisms` | G2 `Evidence`, Any sufficient (level 2) |
| 5 | **+ Add support**, Support group `ANY SUFFICIENT: Evidence` | S2 `Studies show +13% output` |
| 6 | **+ Add support group**, Supports `ANY SUFFICIENT: Evidence` | G3 `Survey findings`, All required (level 3) |
| 7 | **+ Add support**, Support group `ALL REQUIRED: Survey findings` | S3 `Survey of 500 managers` |
| 8 | **+ Add support**, Support group `ALL REQUIRED: Productivity mechanisms` | S4 `No commute saves energy` |
| 9 | **+ Add support group**, Supports `[SUPPORT] No commute saves energy` | G4 `Why commuting drains energy`, All required (sub-argument) |
| 10 | **+ Add support**, Support group `ALL REQUIRED: Why commuting drains energy` | S5 `Average commute is 54 minutes` |
| 11 | **+ Add counter**, Counters `[SUPPORT] Survey of 500 managers` | C1 `Self-reported data` (deep, under G3) |
| 12 | **+ Add counter**, Counters `[SUPPORT] Studies show +13% output` | C2 `Only one study, at one company` |
| 13 | **+ Add rebuttal**, Rebuts `[COUNTER] Self-reported data` | R1 `Answers were checked against delivery records` |
| 14 | **+ Add counter**, Counters `[SUPPORT] Average commute is 54 minutes` | C3 `Many commuters rest on the train` |
| 15 | **+ Add rebuttal**, Rebuts `[COUNTER] Many commuters rest on the train` | R2 `Most commuters drive` |

After step 10 the outline should look like this, with the levels shown by indentation (compare with the
outline reference in the plan's *Decided approach*):

```
[THESIS] Remote work boosts productivity                OPEN
  + ALL REQUIRED: Productivity mechanisms               (holds)
      [SUPPORT] Fewer interruptions                     (standing)
      + ANY SUFFICIENT: Evidence                        (holds)
          [SUPPORT] Studies show +13% output            (standing)
          + ALL REQUIRED: Survey findings               (holds)
              [SUPPORT] Survey of 500 managers          (standing)
      [SUPPORT] No commute saves energy                 (standing)
          + ALL REQUIRED: Why commuting drains energy   (holds)
              [SUPPORT] Average commute is 54 minutes   (standing)
```

Within a group, supporting statements and sub-groups keep creation order, so G2 sits between S1 and S4.

## 3. Expected statuses after each step

Worked out from the AC-2 rules. A group's members are its supporting statements and its non-empty sub-groups; empty
groups are ignored. ALL REQUIRED holds when every member stands or holds, ANY SUFFICIENT when at least one does. A
support stands when no unrefuted counter targets it and every non-empty group directly beneath it holds. Thesis:
UNSUPPORTED when every group directly under it is empty; COUNTERED when an unrefuted counter targets it or a non-empty
group directly under it does not hold; TESTED when any counter exists; otherwise OPEN.

Every support not listed in the "Not standing" column shows `(standing)`. Bold marks what changed in that step.

| # | G1 | G2 | G3 | G4 | Not standing | Counters / rebuttals | Thesis badge |
|---|---|---|---|---|---|---|---|
| 1 | | | | | | | **UNSUPPORTED** |
| 2 | **empty** | | | | | | UNSUPPORTED |
| 3 | **holds** | | | | | | **OPEN** |
| 4 | holds | **empty** | | | | | OPEN |
| 5 | holds | **holds** | | | | | OPEN |
| 6 | holds | holds | **empty** | | | | OPEN |
| 7 | holds | holds | **holds** | | | | OPEN |
| 8 | holds | holds | holds | | | | OPEN |
| 9 | holds | holds | holds | **empty** | (S4 still standing: an empty sub-argument is ignored) | | OPEN |
| 10 | holds | holds | holds | **holds** | | | OPEN |
| 11 | holds | holds (S2 rescues it) | **does not hold** | holds | **S3** | C1 unrefuted | **TESTED** |
| 12 | **does not hold** | **does not hold** | does not hold | holds | S3, **S2** | C1, C2 unrefuted | **COUNTERED** |
| 13 | **holds** | **holds** (via G3) | **holds** | holds | S2 | **C1 refuted**, R1 unrefuted, C2 unrefuted | **TESTED** |
| 14 | **does not hold** | holds | holds | **does not hold** | S2, **S5**, **S4** (by its sub-argument; no counter targets S4) | C2, **C3** unrefuted | **COUNTERED** |
| 15 | **holds** | holds | holds | **holds** | S2 | **C3 refuted**, R2 unrefuted | **TESTED** |

Steps 11 to 13 show a counter three levels deep reaching the thesis badge and being undone by a rebuttal. Steps 14
and 15 do the same through a sub-argument.

## 4. Checklist

Tick each item, or write a finding (section 8).

Layout:

- [ ] Indentation makes every group's parent obvious: G1 under the thesis, G2 under G1, G3 under G2, and G4 under
      S4, not under G1.
- [ ] A sub-argument (G4) is clearly distinguishable from a sibling sub-group (G2), even though both are one level
      below something in G1.
- [ ] Type labels and names read naturally, for example `ALL REQUIRED: Productivity mechanisms`, compared with the
      outline reference.
- [ ] Markers and badge are easy to spot and line up with the right rows at every depth.

Statuses:

- [ ] After each of steps 1 to 15, the group markers, support markers, counter and rebuttal markers and the thesis
      badge match section 3.
- [ ] Step 14: S4 shows `(countered)` with no counter directly under it. Decide whether that reads clearly enough
      (the cause is G4 `(does not hold)` beneath it).

Editing (UC-6 step 7). Use **Edit** on a group row; the form has *Name* and *Group type*:

| # | Action | Expected |
|---|---|---|
| 16 | Edit G2: name `Evidence from studies`, type All required, **Save** | Row reads `ALL REQUIRED: Evidence from studies (edited) (does not hold)` (S2 is still countered); G1 does not hold; thesis **COUNTERED** |
| 17 | Edit G2 again: type Any sufficient only, **Save** | G2 `(holds)` and still `(edited)`; G1 holds; thesis **TESTED** |

- [ ] `(edited)` appears on the group after the save, and stays after a reload.
- [ ] The statuses are recomputed right after the save, as in the table.

Validation (UC-6 A2 and E1). Open **+ Add support group**, choose any target, and post:

| Name | Expected |
|---|---|
| empty | Error next to *Name*: "A support group needs a name of 1 to 100 characters." Nothing is added. |
| three spaces | Same error (the name is trimmed). |
| 101 characters: `12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901` | Error next to *Name*: "Name must be at most 100 characters (it has 101)." Nothing is added. |
| 100 characters (optional): the same string without its last `1` | Group added. Under any group it is empty, so no status changes; delete it afterwards. |

- [ ] Each error appears next to the *Name* field, not only at the top of the form or not at all (a missing inline
      error is a finding, UC-6 E1).
- [ ] The same errors appear in a group's **Edit** form when its name is cleared or made 101 characters long.

## 5. Optional: seed through the API with curl

Same seed as section 2, for a quick rebuild or a second discussion. Run it in bash (Git Bash on Windows) with the API
running; it needs only `curl`, `grep` and `cut`. Then open the printed link.

```sh
API=http://localhost:5080/api/v1/discussions
first() { grep -o "\"$1\":\"[^\"]*\"" | head -n 1 | cut -d '"' -f 4; }
post() { curl -sS -X POST "$1" -H 'Content-Type: application/json' -d "$2"; }

D=$(post "$API" '{"title":"Remote work and productivity","topicTags":["work.remote"],"thesis":"Remote work boosts productivity"}')
ID=$(echo "$D" | first id); T=$(echo "$D" | first thesisId)
group() { post "$API/$ID/statements" "$(printf '{"kind":"supportGroup","parentId":"%s","name":"%s","groupType":"%s"}' "$1" "$2" "$3")" | first id; }
add() { post "$API/$ID/statements" "$(printf '{"kind":"%s","parentId":"%s","text":"%s"}' "$1" "$2" "$3")" | first id; }

G1=$(group "$T" "Productivity mechanisms" allRequired)            # step 2
S1=$(add supportingStatement "$G1" "Fewer interruptions")          # step 3
G2=$(group "$G1" "Evidence" anySufficient)                         # step 4
S2=$(add supportingStatement "$G2" "Studies show +13% output")     # step 5
G3=$(group "$G2" "Survey findings" allRequired)                    # step 6
S3=$(add supportingStatement "$G3" "Survey of 500 managers")       # step 7
S4=$(add supportingStatement "$G1" "No commute saves energy")      # step 8
G4=$(group "$S4" "Why commuting drains energy" allRequired)        # step 9
S5=$(add supportingStatement "$G4" "Average commute is 54 minutes") # step 10
echo "Open http://localhost:5173/discussions/$ID"
```

Steps 11 to 15 are best done in the browser so you can watch each change. Through the API they are:

```sh
C1=$(add counterArgument "$S3" "Self-reported data")                          # step 11
C2=$(add counterArgument "$S2" "Only one study, at one company")              # step 12
R1=$(add rebuttal "$C1" "Answers were checked against delivery records")      # step 13
C3=$(add counterArgument "$S5" "Many commuters rest on the train")            # step 14
R2=$(add rebuttal "$C3" "Most commuters drive")                               # step 15
curl -sS "$API/$ID"   # status, and stands / holds / refuted per statement
```

A group edit through the API (step 16): `PATCH $API/$ID/statements/$G2` with
`{"name":"Evidence from studies","groupType":"allRequired"}`.

## 6. Unnamed group (UC-6 A1, needs PostgreSQL)

Groups stored before the AC-3 migration (`20261107184914_NamedSupportGroups`) have `name` `NULL`. That migration added
the nullable column `name` (`character varying(100)`) to the table `statements`; the group's id is the `uuid` column
`id`. Get an unnamed group in one of two ways:

- **One SQL statement (simpler).** Run the API on PostgreSQL, seed the discussion (section 2 or 5), and take the id
  of G4 (`echo $G4` after section 5, or the `id` of the group in `GET /api/v1/discussions/{id}`; the discussion id is
  the last part of the browser URL `/discussions/{id}`). Then, in `psql` or any SQL client connected to that
  database:

  ```sql
  UPDATE statements SET name = NULL WHERE id = '<G4 id>';
  ```

  The update must report exactly one row.
- **A database from before AC-3.** Run the API from commit `989e6e7` (AC-2, the last commit before the AC-3
  migration, for example from a separate `git worktree`) against a new, empty database, add groups (they have no
  name then), stop it, and start this branch's API on the same connection string. The migration runs at startup and
  leaves those groups with `name: null`.

Reload the discussion page, then:

- [ ] The group row reads `ALL REQUIRED: Unnamed group` with an **Add a name** control, and its marker is unchanged
      (`(holds)` for G4 after step 15). The statuses of the rest of the tree are unchanged.
- [ ] The add-form pickers list it as `ALL REQUIRED: Unnamed group`.
- [ ] **Q-1:** select **Add a name**, leave *Name* empty, change only the type to Any sufficient, and **Save**. Under
      the strict reading (what the API does now) this is rejected with an error next to *Name*: "This support group
      has no name yet; send a name of 1 to 100 characters with the change." Decide if that is right (section 7).
- [ ] Type a name, for example `Why commuting drains energy`, and **Save**. "Unnamed group" and **Add a name** are
      replaced by the name, the row shows `(edited)`, and the marker is unchanged unless the type was changed too.

## 7. Open questions to observe and decide

- **Q-1** (type-only change on an unnamed group): see section 6. Strict reading, rejected with an error on *Name*.
  Accept or ask for the looser reading (reject only requests that clear a name).
- **Q-3** (unchanged value still marks the group edited). The API sets `editedAt` even when the name or type sent is
  the same (README). The web edit form only sends changed fields, and **Save** without changes closes the form
  without a request, so the browser never shows this case. To see it, send the current name again through the API
  and reload; the group shows `(edited)`, or a newer edit time on hover:

  ```sh
  curl -sS -X PATCH "$API/$ID/statements/$G3" -H 'Content-Type: application/json' -d '{"name":"Survey findings"}'
  ```

  Decide whether that is acceptable.
- **Q-2** and **Q-4**, from the earlier tasks, implemented as the README describes: a group `PATCH` with neither
  `name` nor `groupType` (for example `{}`) returns 400 naming `body`, and name length is counted in UTF-16 code units
  (an emoji made of a surrogate pair counts as 2). Confirm or record a change.

## 8. Record the result

Write the result as a comment on the pull request for the AC-6 merge:

- **Sign-off:** "AC-6 checked on <date>, in-memory and PostgreSQL, <browser>. Layout and statuses read clearly.
  Q-1: <decision>. Q-2: <decision>. Q-3: <decision>. Q-4: <decision>."
- **Findings:** for each one, the step number, what you saw, what you expected (section 3 or 4), and a screenshot if
  it helps. A confusing layout or a wrong status blocks the merge until it is fixed and this check is repeated
  (UC-6 E2). Wording or styling suggestions that do not block can be recorded as follow-ups.
