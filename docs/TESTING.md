# Testing

| Layer | Project / folder | Tools | Needs |
|---|---|---|---|
| Backend unit | `tests/ClassHub.UnitTests` | xUnit v3, Shouldly | nothing |
| Architecture | `tests/ClassHub.ArchitectureTests` | xUnit v3, NetArchTest | nothing |
| Backend integration | `tests/ClassHub.IntegrationTests` | xUnit v3, `WebApplicationFactory<Program>`, Testcontainers.MsSql, Respawn | **Docker** (Linux containers) |
| Frontend | `web/src/**/*.test.ts(x)` | Vitest, Testing Library, jsdom | Node |
| E2E | `web/e2e` | Playwright (Chromium: Desktop Chrome + Pixel 7) | Docker, Node, .NET SDK |

## Running

```powershell
dotnet test --solution ClassHub.slnx -c Release                 # everything (Docker must be running)
dotnet test --project tests/ClassHub.UnitTests                   # no Docker
dotnet test --project tests/ClassHub.IntegrationTests            # Docker
cd web; npm test
```

Tests run on Microsoft.Testing.Platform (`global.json`). The first integration run pulls the SQL Server image (~1.7 GB). A full run takes about 40 seconds afterwards.

## Frontend tests (R0-P6)

- `src/test/render.tsx` → `renderRoute(path, language?)` renders the real route tree in a memory router with a fresh QueryClient, in Macedonian unless a language is given. Tests query by role and the visible (translated) name.
- `src/api/client.test.ts` drives the API client against a fake `fetch`: antiforgery fetch-before-unsafe, token adoption after sign-in, one retry on `ANTIFORGERY_INVALID`, ProblemDetails → `ApiError`, network errors, cookies.
- `src/lib/format.test.ts`: DST-aware time zones, mk/en dates, money with currency, translated error codes.
- Backend side: `Platform/SpaHostingTests` (integration) checks the fallback only for SPA prefixes, cache headers, and that `/api` and health checks never return the SPA.

## End-to-end tests (Playwright, R0-P7)

```powershell
cd web; npx playwright install chromium   # once
npm run e2e                                # builds the SPA and the API, then runs all projects
```

`e2e/global-setup.ts` starts the whole stack and returns its teardown:

1. A disposable SQL Server 2022 container (same image as the integration tests) with a generated `sa` password, published on a random **loopback** port.
2. The production SPA build (`npm run build` → `wwwroot/dist`) and a Release build of the API (skipped with `E2E_SKIP_BUILD=1`, as CI builds them in earlier steps).
3. The API (`dotnet ClassHub.Api.dll`) in the **E2E** environment on `http://127.0.0.1:5180` (`E2E_PORT`), with `App:PublicBaseUrl` pointing at itself so email links open the SPA. At startup it migrates the database, but `E2EDatabase.AssertDisposable` refuses anything except a `ClassHubE2E_*` database on a loopback TCP endpoint with SQL authentication: the local `ClassHubDb` (Windows authentication, `.\SQLEXPRESS`) can never be used.

Flows (each in **mk and en**, on **desktop and mobile**): register → dev mailbox → confirm → sign in → the dashboard shows the workspace → sign out; forgot password → reset link → the old password fails, the new one works; a second teacher opening the first teacher's workspace URL sees "not found". Texts come from the app's locale files, so wording changes do not break tests. CI job `e2e` uploads the HTML report and traces on failure.

On a machine with little free memory, many local workers can exhaust Chromium's blob storage: a request then fails in the browser (`net::ERR_BLOB_OUT_OF_MEMORY` in the trace's network log) and the app shows "no connection to the server", in any test. Run with fewer workers (`npx playwright test --workers=3`; CI uses 2) rather than retrying.

## Usage analytics tests (R3-P6)

- **Integration:** `UsageTests`.
  - **Opens, creations and reuses are counted for the workspace only.** Two GeoGebra opens and one drawing board open; a created drawing, duplicated and reopened.
  - Unknown tools and other workspaces' activities give 404; an invalid period gives 400.
  - Another workspace sees nothing and gets the cross-tenant 404.
  - **Twelve parallel opens give exactly 12.**
  - **Staff see platform totals only:** the response holds no workspace id; there are 7 days with today last, and teachers get 403.
- **Vitest:** `Usage.test.tsx`.
  - Opening a tool sends the count; the usage card shows the workspace's numbers.
  - A saved activity opened on its board is counted once, a new drawing not at all.
  - The staff page shows totals per tool, board and day (days without use left out) and switches the period.

## Function graph tests (R3-P5)

- **Vitest:** `expression.test.ts` (the safe parser).
  - The usual school terms: precedence, right-associative powers, implicit multiplication, decimal comma, functions and constants. Undefined points are gaps, and stay undefined through nested operations (fails without the propagation).
  - **Malicious and invalid input is refused:** `alert(1)`, `eval(x)`, `Function(...)`, `x.constructor`, `__proto__`, `this`, `window`, `;`, `=>`, `=`, template strings, HTML, unknown variables, missing parentheses, `^^`, `..`, indexing, commas, malformed numbers (`1.2.3`, `1,2,3`) and numbers side by side (`2 3`, `x2`), in both parsers.
  - Limits on length, nesting and size.
- **Vitest:** `construction.test.ts`.
  - Functions named f, g, h …; invalid terms add nothing; remove; version 1 documents open as version 2.
  - Graphs are drawn as fixed curves of plain functions (never a string for JSXGraph to evaluate, never draggable away from the term).
- **Vitest:** `GeometryBoardPage.test.tsx`: an invalid term shows the hint and adds nothing; valid graphs are listed, removed and saved in version 2.
- **Unit:** `ActivityRulesTests`.
  - The C# grammar accepts and refuses the same cases, with limits; the 200-character limit counts the term as saved (spaces included), on both sides.
  - Function graphs are accepted only in format version 2, with valid terms.
- **Integration:** `ActivityTests`: function objects are refused in version 1 and with malicious terms; a valid version 2 graph is kept as written.
- **E2E:** `geometry.spec.ts`, **plot → save → reopen**. An invalid term shows the hint; `x^2 - 2` is drawn as a visible SVG path; it is saved in version 2, reopened from the library and saved again unchanged. No CSP or eval errors.

## Geometry Board tests (R3-P4)

- **Unit:** `ActivityRulesTests`: constructions with known objects on distinct, earlier points and a valid view. Missing format, wrong coordinates, duplicate names, self-references, forward references, unknown types (a `function`), unsafe names and a malformed view are refused. `ToolRulesTests`: only shipped boards can be active.
- **Integration:** `ActivityTests` refuses constructions without a format, with missing points or with function objects. `ToolboxTests` lists both boards.
- **Vitest:**
  - `construction.test.ts`: naming (A … Z, A1); objects built only from distinct existing points; moving points; JSON round-trip; damaged documents opened defensively.
  - **The construction ↔ board mapping** (`render.ts`, with a fake board): JSXGraph types and parents, points first, the angle's vertex in the middle.
  - `GeometryBoardPage.test.tsx` (JSXGraph replaced by a stand-in that reports clicks and drags): a triangle from three clicks; undo and Ctrl+Z, one step per click; a drag that moves several points is one step and all of them are saved; at the 2 000-object limit a click that would exceed it changes nothing and shows the message (fails without the check); the saved construction in ClassHub's format.
- **E2E:** `geometry.spec.ts`, **triangle → save → reopen** on a desktop and a tablet. Three clicks build the triangle; it is saved; dragging the triangle by its inside moves all three points by the same offset, and that is what is saved; it is reopened from the library and saved again with its points and triangle. No CSP violations; nothing completes to another host.

## Drawing Board tests (R3-P3)

- **Unit:** `ActivityRulesTests`: drawing content (allowed element types only; no images, iframes, embeds or files; `elements` required; `appState` an object).
- **Integration:** `ActivityTests` refuses drawings without elements or with images, embeds, files or untyped elements. `ToolboxTests` sees the drawing board in the toolbox, and the geometry board still hidden.
- **Vitest:**
  - `scene.test.ts`: the scene round-trips unchanged; images, embeds and deleted elements are never saved; damaged content opens as an empty or partial drawing; disallowed elements are removed from the scene; pastes with images, embeds or files are refused.
  - `DrawingBoardPage.test.tsx` (Excalidraw replaced by a stand-in with the same contract): the first save creates the activity without images and the page becomes it, with no reload; later saves change it; a reopened activity saves with the loaded version. After a refresh or Back (navigation state left over) the saved activity is loaded, never an empty board; a reopened board starts from the latest version, never a cached older one (both fail on the code before); the saved confirmation shows only while the board matches what was saved (an edit after or during the save clears it).
- **E2E:** `drawing.spec.ts`, **draw → save → reopen** on a desktop and on a tablet (iPad).
  - Draw a rectangle; a paste from another board carrying an image is refused with a message (fails without the guard); save it, open it again from the library and save again: only the rectangle is in the document.
  - The board's font loads from ClassHub; no request to another host completes, and every foreign attempt was blocked by the CSP.

## Activity Library tests (R3-P2)

- **Unit:** `ActivityRulesTests` (supported boards and format versions; documents are objects within the limit in UTF-8 bytes; copy titles always fit).
- **Integration:** `ActivityTests`.
  - **Save, find (title, subject, board), open, change with the version** (stale: 409; the board never changes), duplicate and delete. The document round-trips unchanged; a deleted activity cannot be opened.
  - **Documents must be JSON objects** in a supported format, with a title, a known board and a catalog subject, **within the size limit** (lowered to 100 bytes in a derived host).
  - **The Free limit holds under 13 parallel saves**: exactly 10 are created, 3 get `PLAN_LIMIT_REACHED`. A duplicate is limited too; deleting frees a place.
  - **Other workspaces see nothing:** 404 under the owner's route and under their own; their list is empty.
- **Vitest:** `ActivityLibraryPage.test.tsx`.
  - List, board labels and search.
  - Rename sends the listed version and the unchanged document.
  - No open link before the board ships; an open link once the toolbox lists it.
  - The plan-limit message on duplicate; delete only after confirming, with the version.
  - Deleting the only activity of the last page moves back to the page before it (fails without the move).

## Toolbox tests (R3-P1)

- **Unit:** `ToolRulesTests`.
  - Allowed: official HTTPS links on approved domains and their subdomains.
  - Refused: other schemes, ports, credentials, look-alike and parent domains, IP hosts, overlong links.
- **Integration:** `ToolboxTests`. The catalog is shared by all tests, so each test uses its own codes and words.
  - Teachers find the seeded tools by search (also the Macedonian description), subject and category. Inactive built-in boards are hidden; featured tools come first. An invalid category or an overlong search is a 400.
  - **A tool for any subject is found under a subject filter** (fails with the old filter). License notes come in both languages.
  - **Staff add, feature and deactivate a tool:** teachers then no longer see it. A stale version is a 409; the code is permanent and unique. All changes are audited, with every managed field before and after: a change of the description or license alone shows the old and the new text (fails with the shorter snapshot).
  - **Only official HTTPS links on approved sites** and known boards are accepted; each board has one entry. Kinds and categories are names, never numbers. **A board that has not shipped cannot be activated.**
  - **Only platform staff manage tools:** a teacher gets 403, an anonymous caller 401.
- **Vitest:** `ToolboxPage.test.tsx`.
  - Cards, search and category filters, and the external link with `target` and `rel`.
  - Admin: add an external tool, and edit one with the version the form was opened with: a refetch while editing never makes stale values look current (fails when the latest version is sent).
- **E2E:** `toolbox.spec.ts` (desktop and phone). From the navigation, search for GeoGebra and open it. It opens in a new tab at the official URL, and `window.opener` is null. The site is answered by a local route, so the test never leaves the machine.

## Dashboard tests (R2-P12)

- Integration: `TeachingDashboardTests`:
  - zeros and six empty months for a new teacher;
  - **the numbers match a fixture**: lessons in and outside the 7 days, a cancelled one, an archived student with a lesson held without a package, debts in two currencies, a running-low package, payments this month and last month;
  - **every student running low is counted** although only the first 10 are listed (a Pro workspace with 12);
  - **another teacher sees none of it** and gets the cross-tenant 404;
  - **a student sees only their own lessons**: no other student's lesson, no cancelled one, the meeting link only ahead, attendance and the shared summary once held, never the private note; their attendance counts; 404 for another workspace's portal and for a teacher on the student route.
- Vitest:
  - `TeachingOverview.test.tsx`: tiles and links, next lessons, debts per currency, running low, the income table, and the empty states.
  - `StudentPortalPages.test.tsx`: next lesson with its join link, later lessons, past lessons with attendance and summary, the attendance sentence.

## Notification tests (R2-P11)

- Unit: `NotificationRulesTests` (keys per change: lesson revision, reminder start and attempt, homework assignment; reminder timing incl. late planning and the last hour, optional kinds and preferences, lesson times local to the lesson's zone in summer and winter and in another zone).
- Integration: `NotificationTests` (a new lesson announced once to the account; **cancel → the pending reminder is withdrawn, only who was told hears it is off, never a reminder**; **reschedule → exactly one new reminder** with the new time; preferences switch off reminders and homework emails but not lesson changes, and an opted-out reminder is not planned again on later passes; **homework removed and assigned again is announced once**; **first preference saves at the same moment all apply** (fails without the retry on the insert conflict); **recipients resolved at send time** (changed email, archived student, no address); **five parallel dispatchers send every notification exactly once**; **a pass drains a backlog larger than one batch** (fails with a single batch per pass); group lessons: a student taken out hears it is off, the others are not told twice; homework emails link to the portal; **a student taken out and added back is told again**, and **taken out and back before sending gets no cancellation** (fails without the withdrawal and the send-time check) and **a lesson moved away and back gets a reminder again** (both fail with keys per lesson and student only); **a series is announced once, and ending it sends one series email** (plus the cancellation of the reminded lesson, never one per lesson); **ending a series names the individually changed lesson that stays** (fails without it); **a series starting beyond the horizon is announced with its first lesson**; **a lesson of an announced series is told when cancelled**; **lessons and series known without our announcement are told when cancelled**, a lesson cancelled before its announcement went out is not (fails when only sent emails count as told); **cancelling a series' first lesson keeps its announcement** (naming the next lesson); **a removed student hears about the lesson as they knew it** (fails when the edited details are used); **a reminder found outdated is replaced when the lesson is back at its time** and **a reminder delayed into the last hour is not sent** (both fail on the code before), also when it reaches the last hour during a long pass (fails with the pass-start time); **reminders are planned for the whole window page by page**). The test host runs the dispatcher only when a test calls it.
- Vitest: `StudentPortalPages.test.tsx` (notification choices saved with the antiforgery header).

## Finance tests (R2-P10)

- Unit: `FinanceRulesTests` (balances per currency incl. reversals and credit, the consumption policy table, oldest open package with a lesson left, corrections within the package); `AppendOnlyTests` (architecture: no bulk or tracked changes of ledger rows, with detector cases).
- Integration: `FinanceTests` (offer terms snapshotted and unchanged by a later price change, custom package validation; **a held lesson consumes once per present student, attendance changes reverse and re-consume**; **concurrent completions and parallel job runs consume once**; **two events of one lesson handled at the same time consume once** (fails without the student lock and the unique lesson entry); **a worker never consumes on attendance another worker saw corrected** (the teacher corrects the attendance between a worker's read and its action; fails without the lesson lock); a held lesson is given back by reversing it, corrections cannot go below held lessons, and a waived lesson does not consume again; missing or extreme numbers are 400; unpaid lessons without an open package (D-25), closed packages unused, cancelled lessons never consume; **partial payments, reversal once, currencies kept apart**, invalid payments; corrections with reasons and range checks, reversal of corrections, **audit entries with the actor**; the ledger refuses updates and deletes; overview and period report per currency (lessons consumed include corrections not linked to a lesson); **closing a package waits for a consumption in progress** (fails without the student lock); **the whole history stays reachable page by page** and an old payment can still be reversed; isolation). The test host runs the consumption job only when a test calls it.
- Vitest: `FinancePages.test.tsx` (balances, packages and history; history pages; a custom package in a typed currency; package from an active offer; payment in minor units with amount checks; reversal with a reason, conflict and range messages; balance correction sign; overview and report without totals across currencies).
- E2E: `finance.spec.ts` (custom package → a held lesson uses one lesson in the background → partial payment → balance on the student page and the finance page).

## Homework tests (R2-P9)

- Unit: `HomeworkRulesTests` (submit and resubmit, review needs an answer and makes it final, comment changes keep the review time, late flags, the end of the due day including clock changes and a skipped midnight); `DocumentFilesTests` (the shared upload rules, moved from materials).
- Integration: `HomeworkTests` (submit with a file, resubmit keeping the file, **a stale form (older version) is 409 and a missing version 400**, teacher download, **a review of an older version is 409**, reviewed is final; **late answers accepted and flagged**, a later due date clears the flag; **cross-student access is 404** (list, detail, file, submission), teacher routes closed to students, other workspaces and anonymous visitors; archived homework leaves the portal and takes no answers; **homework archived while an answer file is uploaded refuses the answer and keeps nothing** (fails without the locked re-check); **file limits**: disallowed type, empty answer, replacement and removal of the file with the blob, plan storage full → `PLAN_LIMIT_REACHED` with nothing left behind; group assignment snapshot, parallel repeats, unknown and archived students; answered assignments cannot be removed, reviews need an answer; lesson link validation and the lesson filter).
- Vitest: `HomeworkPages.test.tsx` (list with counts; create for a lesson; assign students and groups, remove; answer with file, late flag, review with the seen version; conflict message; portal list, multipart answer and resubmission, storage-full message, reviewed state).
- E2E: `homework.spec.ts` (assign → the student hands in text and a file on a phone → the teacher downloads and reviews → the student sees the review and can no longer change the answer).

## Materials tests (R2-P8)

- Unit: `DocumentFilesTests`, formerly `MaterialFilesTests` (every allowed kind by extension and signature, Office packages whatever their entry order and only with their own main part; mismatching signatures, short or binary files, HTML, SVG, executables and names without an extension refused; safe names: path parts, reserved and control characters, dots and spaces, the detected extension, empty names, the length limit).
- Integration: `MaterialTests` (upload and download as a safe attachment with nosniff, sandbox CSP and no-store, images inline; disallowed types and mismatching signatures → `FILE_NOT_ALLOWED`, empty file and missing title refused, nothing stored; **storage quota → `PLAN_LIMIT_REACHED` with no row and no blob left behind**, other workspaces unaffected; delete frees the storage and removes the blob; **students open only materials assigned to them or their current groups, unassigned → 404**, leaving the group, archiving and unassigning end access, another student sees nothing; assignment validation, foreign students, parallel repeats make one assignment, archived students refused; isolation for other workspaces, anonymous visitors and uploads without the antiforgery header; details with version check, subject validation and search).
- Vitest: `MaterialsPages.test.tsx` (list with storage used; subject filter; client-side file checks then multipart upload with the antiforgery header and the new material opened; assign to a student and a group, assigned group not offered again, unassign; conflict on save keeps the edits; archive with the version, delete after confirming; portal list with download links and the empty state; allow-list and size formatting).
- Integration: `TeacherProfileTests` (a profile photo is refused with `PLAN_LIMIT_REACHED` when materials filled the storage; replacing the photo at exactly the limit succeeds because the replaced photo counts as freed; both fail on the code before the shared quota).
- E2E: `materials.spec.ts` (the teacher uploads and assigns a PDF; the student accepts the invitation on a phone, sees it in the portal and downloads it with its name; an unassigned material and the teacher routes are 404 for the student).

## Attendance tests (R2-P7)

- Integration: `AttendanceTests` (completion records attendance with Present by default and one `LessonCompleted` event, repeated completion returns the held lesson and records nothing; **six parallel completions → one transition, one event, all answered with the held lesson** (fails when a lost race is not answered idempotently); attendance corrected only on held lessons, `AttendanceChanged` event, strangers and unknown statuses refused; a cancelled lesson is never held; lessons held before attendance existed are backfilled as Present by the migration's statement; notes with version check and limits; history per student and group, newest first with attendance, validation and isolation (another workspace's student or group is 404, not an empty history)).
- Vitest: `Lessons.test.tsx` (held with per-student attendance, then correction and notes; unsaved attendance changes survive a notes save; history with attendance on the student page).
- E2E: `lessons.spec.ts` (mark a past lesson held with an excused student; the student's history shows it).

## Booking tests (R2-P6)

- Unit: `BookingSlotsTests` (step and fit, busy lessons with touching allowed, notice, days off, weekdays, past and horizon, several intervals, the spring clock change, a lesson must end within the working hours in real time on that day).
- Integration: `BookingTests` (page, slots, site link and booking page; two visitors may request the same time (D-23); confirm creates the student and the lesson and emails the visitor; the other request then conflicts and is declined; **parallel confirmations (same request and overlapping requests) create exactly one lesson** (fails without the lock); reschedule and cancel with emails; changing a booked lesson in the lesson editor keeps its booking in step; a confirmation that fails converts nothing; a request matching an archived student cannot be confirmed (nothing changes); requests are paged; only free slots of public offers whose duration a lesson can have, booking off refused; honeypot and per-site cap; settings validation (missing and null fields are 400), stale version, isolation).
- Vitest: `BookingWidget.test.tsx` (book a time through the protected form without cookies, a just-taken time reloads the slots, field checks, booking off → contact form, text parity) and `BookingPage.test.tsx` (confirm, conflict message then decline with a message, cancel needs a reason, settings saved with the loaded and then the new version, requests paged).
- E2E: `booking.spec.ts` (a visitor books on a phone from the site link; the teacher confirms and opens the lesson).

## Recurring lesson tests (R2-P5)

- Unit: `SeriesPatternTests` (weekly/bi-weekly weekdays, end by count and date, same local time across the autumn change, the skipped spring hour has no instant, first index on or after a date).
- Integration: `LessonSeriesTests` (12 weeks created, the horizon job extends idempotently and stops at the count; an overlapping occurrence skipped and reported; this-and-following keeps earlier, held, cancelled and individually changed lessons and splits the series; all future replaces upcoming lessons, and a refused edit (archived student) changes nothing; ending removes upcoming lessons and the job respects it; replaced series and stale versions refused; **two series for the same slot created in parallel never double-book** (fails without the lock); series that cannot progress (teacher left, nothing left before the end date) never starve the job, with a batch of one; a date-ended series catches up after the job was down; changing or ending a series keeps a lesson that already started today; after a workspace time zone change, an edit reads its times in the zone the form showed (series or workspace zone only); this-and-following cuts at the selected lesson itself even when the series' and the form's calendars are two days apart (Pago Pago / Kiritimati); validation and isolation).
- Vitest: `Series.test.tsx` (weekly series from the editor; this and following keeps the lessons left of a counted series; series page with skipped dates, all-future change, ending after confirming; a replaced series links to its continuation; "this and following" never falls back to a single-lesson save while the series is loading; a counted series that was ended early sends and offers one end, the earlier one; "all future" starts from the next lesson that has not started (in the series' own zone) and counts only those left).
- E2E: `lessons.spec.ts` also schedules four weekly lessons, sees them in the month, and ends the series.

## Lesson tests (R2-P4)

- Unit: `LessonRulesTests` (state machine, half-open overlap incl. touching and containment, cancelled lessons free time, CET/CEST conversion, the skipped spring hour refused, the repeated autumn hour = first occurrence, 23/25-hour days).
- Integration: `LessonTests` (local → UTC scheduling and range reads; overlap refused, touching allowed; **five parallel overlapping creates → exactly one** (fails without the lock); complete only once started and only once, cancel needs a reason and frees the time, final states cannot change or be edited; rescheduling excludes itself, replaces participants, version conflicts; group lessons take active members only, empty group refused, student/group filters; DST gap → 400 and a lesson across the autumn change keeps its real length and day; validation (range, duration, https link); after a workspace time zone change, lessons show and group by day in the new zone and saving keeps the instant, also for an unchanged start in the repeated autumn hour; isolation, foreign students refused, another teacher's schedule independent, composite FK rejects a cross-tenant participant (547)).
- Vitest: `Lessons.test.tsx` (date helpers; week view in local time, navigation and month/day views; scheduling for the chosen day; overlap message and a group lesson; unsaved edits survive a refetch and conflict on save; cancel with a reason → read-only; "held" becomes available when the lesson starts while the page is open; the chosen student keeps their own name when a new search no longer lists them). Editors of students, groups and lessons now assert that a newly created entity opens (it stayed on "loading" before).
- Vitest: `useStarted.test.ts` (fake timers: the start arrives; after rescheduling the delay is measured from the real current time; a start already past is noticed).
- E2E: `lessons.spec.ts` (schedule from the calendar, see it in the week, overlap refused, month count; no horizontal overflow on phones).

## Student account tests (R2-P3)

- Integration: `StudentAccountTests` (invite → preview → create an account → portal; the student is invisible to teacher endpoints (404); the link works once; an existing account linked to students of two teachers (D-16); one account cannot be two students of one teacher (409); `student:self` gives 404 to other workspaces, to teachers without a student record, and 401 anonymously; unlink and archive end access and void pending links; a new invitation revokes the old one; expired links; a minor needs the guardian agreement (recorded); an existing email must sign in (409); **three accounts accepting one invitation at once → exactly one linked** (fails without the lock); cross-tenant invite/unlink → 404; email required and 5 invitations per day).
- Vitest: `StudentPortalPages.test.tsx` (create an account from the link, fragment cleared; guardian agreement; existing email → sign in with the token kept; accept while signed in; invalid link; teachers list, own page, 404 for another workspace; `/app` without a workspace → portal); `StudentsPages.test.tsx` account card (invite, withdraw, remove access after confirming).
- E2E: `student-accounts.spec.ts` (teacher invites → student opens the email link on a phone, creates the account, sees the portal; the teacher sees the linked account).

## Group tests (R2-P2)

- Integration: `GroupTests` (build a group and put a student in several groups; adding twice is a no-op and leaves the version; version conflicts; remove/re-add/archive keep the rows; archived groups take no members; a student of another workspace is rejected by the API (400) and by the composite FK (547); archived students are blocked; isolation; **three parallel adds for the last of 100 places → exactly one**; re-adding a former member to a full group is refused). The isolation sweep covers the new routes automatically.
- Vitest: `GroupsPages.test.tsx` (list with counts, create with validation, add/remove members while unsaved details survive and save with the loaded version, picker search, conflict on save after a refetch, archive, groups on the student page).

## Student tests (R2-P1)

- Integration: `StudentTests` (add/edit/archive/restore a minor with guardian and subjects, version conflicts, search; isolation; unique email among a teacher's students incl. archived, separate per teacher; conversion creates once, is idempotent and links later requests by email; **five parallel conversions of one request create exactly one student**; Free limit of 10 active students with three parallel adds for the last place → exactly one, conversion blocked at the limit, archived students do not count, restore blocked at the limit; private notes not in student responses; validation).
- Unit: `StudentNamesTests` (name split, length cuts, email normalization); redaction cases for student fields.
- Architecture: `StudentPrivacyTests` (no Students contract except the notes ones carries a note).
- Vitest: `StudentsPages.test.tsx` (list and search, add a minor with guardian and the duplicate-email message, edit with the loaded version, archive, notes, conversion from the request page). E2E: `leads.spec.ts` now ends with the conversion into a student.

## Release 2 hardening (R2-P13)

- **Release journey, E2E `release2.spec.ts`**, desktop and phone:
  1. A visitor books on the public page.
  2. The teacher confirms it; the visitor becomes a student with a lesson.
  3. The teacher invites the student and assigns a package.
  4. The lesson is held; one package lesson is used in the background.
  5. A partial payment is recorded; the teacher assigns homework.
  6. The student creates the account on a phone and sees their held lesson with "Present", the attendance sentence and the homework.
  7. The teacher's dashboard shows the student, the lessons left and what is owed.

  No horizontal scroll at phone width. This journey found that the new portal lesson response sent its enums as numbers; they are now names like every lesson response (the integration test checks the JSON).
- **Release-level Codex review** over all Release 2 changes (`codex review --base 3c11d1b`, 215 files) found one defect: ending or editing a series failed (500) when homework or a finance entry pointed at one of its upcoming lessons. Now such a lesson is cancelled instead of deleted. Test: `LessonSeriesTests.Ending_a_series_cancels_instead_of_deleting_lessons_that_homework_or_finance_point_at`, which fails with a 500 on the old code.
- **Isolation sweep:** `IsolationSweepTests` also discovers every `/api/student/w/{workspaceId}/…` route. Each one gives:
  - a student of another teacher: 404;
  - the workspace's own teacher, who is not a student there: 404;
  - an anonymous caller: 401.

  The student still reaches their own portal. Workspace routes (including the Release 2 ones: students, groups, lessons, series, booking, materials, homework, finance, the teaching dashboard) were already covered: they are found from the endpoint table.
- **Concurrency sweep:** every concurrency-critical Release 2 rule has an integration test that runs the operations in parallel on SQL Server:

  | Rule | Test |
  |---|---|
  | Free plan student limit under parallel adds | `StudentTests.The_free_plan_limits_active_students_and_parallel_adds_cannot_exceed_it` |
  | A request is converted to one student | `StudentTests.Converting_a_request_creates_the_student_once_and_links_later_requests_by_email` |
  | One invitation links one account | `StudentAccountTests.Two_accounts_accepting_one_invitation_at_once_link_exactly_one` |
  | Group seats | `GroupTests.Parallel_adds_for_the_last_place_fill_the_group_exactly_once` |
  | No double-booked teacher | `LessonTests.Parallel_requests_for_overlapping_times_schedule_exactly_one_lesson`, `LessonSeriesTests.Two_series_for_the_same_slot_created_in_parallel_never_double_book`, `BookingTests.Parallel_confirmations_of_overlapping_requests_create_exactly_one_lesson` |
  | A lesson is held once | `AttendanceTests.Parallel_completions_make_one_transition_and_one_event` |
  | A held lesson consumes once | `FinanceTests.Concurrent_processing_of_a_held_lesson_consumes_it_once`, `FinanceTests.Two_events_of_one_lesson_handled_at_the_same_time_consume_once` |
  | An answer never lands on archived homework | `HomeworkTests.Homework_archived_while_an_answer_is_uploaded_refuses_the_answer` |
  | Every notification is sent once | `NotificationTests.Parallel_dispatchers_send_every_notification_exactly_once` |
  | Preference saves at the same moment | `NotificationTests.First_preference_saves_at_the_same_moment_all_apply` |

## Release 1 hardening (R1-P12)

- Integration: `IsolationSweepTests` discovers every `/api/w/{workspaceId}/…` and `/api/admin/…` route from the endpoint table (new endpoints are covered automatically) and checks: another teacher gets 404 `NOT_FOUND` on every workspace route, anonymous callers get 401, teachers get 403 on every admin route. Requests carry a body in the declared format (JSON or multipart), so authorization is what answers.
- E2E `release.spec.ts`: the whole journey through the UI (sign up → confirm email → profile → offer → publish → a phone visitor's lesson request → the teacher's inbox), with no horizontal scroll at phone width on every teacher page; SEO checks on a sample page (doctype, `lang`, charset, viewport, title, description length, canonical, Open Graph and Twitter tags, one `h1`, image alternatives, named links, JSON-LD, sitemap XML with the page, robots rules and sitemap link, 404 with `noindex`).
- E2E `admin.spec.ts` now also checks that suspending a teacher hides their published page at once, and no horizontal scroll on the admin pages at phone width.
- E2E `lighthouse.spec.ts` (opt-in, `E2E_LIGHTHOUSE=1`, desktop project): runs `npx lighthouse@13.5.0 --only-categories=seo` with Playwright's Chromium on a sample published page; requires a score ≥ 90. Run before a release, not in CI (it downloads Lighthouse). Release 1 result: **100**, no failed audits.

## Moderation & catalog tests (R1-P11)

- Integration: `ModerationTests` (report validation, unknown content, honeypot drop; cap per site per hour under 30 parallel reports; parallel hides of two sections both kept; parallel decisions about one report (dismiss/dismiss, hide/dismiss) let exactly one win; tests and offers keep a deactivated subject they already use; hiding a site removes the page, test page, lead form, sitemap entry and new reports, resolves all open reports about it, tells the teacher the reason without the reporter, survives republishing, restore brings it back, audit; hiding a section removes only that section and the hero photo; hiding a test removes its page, link, sitemap entry and attempts while the teacher's data stays; dismissing once, teachers cannot see the queue; catalog create/duplicate/invalid code, deactivation hides the subject from new choices but keeps saved references, permanent codes, audit).
- Vitest: `ReportAbuse.test.tsx` (mk/en text parity, section report without cookies and with an empty honeypot, validation and rate-limit message), `AdminModeration.test.tsx` (queue with reporter details and hide with reason, restore, catalog deactivate/add, teacher notice without reporter details).

## Plan & entitlement tests (R1-P10)

- Integration: `PlanTests` (Free on registration with the D-29 limits and isolation; the fourth published test blocked on Free, drafts unaffected, Pro raises the limit; **parallel publications near the limit: exactly one of four succeeds**; activation records the payment and extension adds months to the end, audited, overview numbers; activation validation; expiry: limits follow the end date at once, the job moves back to Free once with history and a system audit entry, over-limit tests stay published and a new one is blocked; teachers cannot activate Pro or change limits; owner-only limit changes with version conflict and audit).
- Unit: `SubscriptionPeriodTests` (effective plan, activation, extension from the end, restart after the end).
- Vitest: `PlanPage.test.tsx` (Free limits and usage with the upgrade note, Pro period), `AdminPages.test.tsx` (Pro activation with the payment record, owner-only plan editing, read-only for admins).

## Platform admin & MFA tests (R1-P9)

- Integration: `MfaTests` (enrollment with a wrong and a right code, 202 + no session after the password, the second step, recovery codes once and audited, regeneration with step-up, replacing the authenticator only with a current code or a recovery code, lockout after 5 wrong codes, step-up refused during a lockout on every path and reset by a right code, second step needs the first, exactly one kind of code, keys and codes encrypted at rest, suspended account cannot finish); `PlatformAdminTests` (401/403 for anonymous/teachers, `MFA_REQUIRED` for password-only staff, staff cookie never persistent and `amr` kept across stamp validations, overview numbers, search/detail return metadata only, suspension ends sessions/blocks sign-in/hides the site/is audited with reason and actor, owner vs admin target rules, owner-only role grant/revoke with step-up, staff get 404 on workspace routes, audit filters and hidden workspace details). `PlatformStaffKit` grants roles like the CLI and computes TOTP codes (RFC 6238).
- Unit: `MfaUnitTests` (code normalization, key formatting, staff session limits).
- Vitest: `TwoFactorLogin.test.tsx` (code step, recovery code, expired sign-in), `AdminPages.test.tsx` (no access, enrollment → recovery codes → overview, sign in again, suspension with reason, owner role grant with code, admin without role management, audit filters).
- E2E: `admin.spec.ts` — a Platform Owner bootstrapped with the CLI (`grant-platform-owner` on the disposable database) enrolls the authenticator, signs in again with a TOTP code computed from the shown key, suspends a teacher (session ends, sign-in refused) and finds the audit entry.

## Dashboard tests (R1-P8)

- Integration: `TeacherDashboardTests` (zeros and a full period of days for a new teacher; a fixture with offers, leads in every status incl. archived and old ones, a diagnostic request, test attempts, page views today and 40 days ago → exact numbers for 30 and 90 days, funnel included; only 7/30/90 days; isolation).
- Vitest: `DashboardOverview.test.tsx` (tiles and links, funnel shares, requests by status, period switch requests the new period, daily visits as a table).

## Public test tests (R1-P7)

- Integration: `PublicTestTests` (no correct answers in the raw public JSON, `SelectSeveral`; drafts, inactive and unpublished sites hidden, cached page evicted; server-side scoring incl. partial and empty answers, random attempt ids, anonymous columns, attempt count; tampered submissions refused and not stored; **8 parallel attempts against a cap of 3**; contact with shared and unshared results, teacher views; one request per attempt and only from its own site; type/attempt rules; attempts lock destructive edits; indexable test page, link from the site page, sitemap; isolation of the results view).
- Vitest: `DiagnosticTest.test.tsx` (choices sent without cookies, score, request with the result and opt-in sharing, unanswered hint, busy test, unavailable test, text parity); results card and the lead's test result.
- Playwright `diagnostic.spec.ts`: phone visitor opens the test from the site, answers, sees the score, sends a request sharing the result → the teacher sees it on the lead and in the test's results.

## Diagnostic test builder tests (R1-P6)

- Unit: `DiagnosticTestRulesTests` (scoring: all correct, several correct options all-or-nothing, duplicates, unanswered/unknown, zero-point questions; publish readiness; request validation incl. null questions; slugs).
- Architecture: `PublicProjectionTests` (no correct answers or domain entities in any public projection).
- Integration: `DiagnosticTestTests` (create with generated and suffixed addresses, unique per workspace, ids kept on edit, stale version, foreign question/option ids refused, publish readiness errors, publish/deactivate/republish, **D-38** destructive edits refused and text fixes allowed after attempts, duplicate as editable draft, archive keeps the address, isolation kit on every endpoint, composite FK and points CHECK in the database).
- Vitest: `TestsPages.test.tsx` (list, create with questions/options/points, browser validation, ids kept, publish problems shown, publish after saving with the new version, structure locked with attempts and duplicate, archive with confirmation).

## Lead tests (R1-P5)

- Unit: `LeadRulesTests` (every status transition, form tokens: site-bound, tampered, expired, future, request validation, minimal localized email, consent texts).
- Integration: `LeadTests` (form contents: only public offers and own subjects, `no-store`; full intake → inbox + email **without visitor data**, nothing echoed; honeypot and too-fast submissions dropped; invalid/foreign/expired tokens; field validation incl. hidden/inactive/foreign offers and foreign subjects; per-site hourly limit → 429; D-10b for publishing and leads; unknown/unpublished sites; statuses with versions and invalid transitions; notes; archive/restore; filters, search with literal `%`, paging, new count; isolation kit on every endpoint; composite FK in the database).
- Vitest: `ContactForm.test.tsx` (payload incl. token, consent version and empty honeypot without cookies; browser validation; hidden when unavailable; expired form renewed; rate limit message; mk/en text parity), `LeadsPages.test.tsx` (list, filters in the URL, archived, search, empty state; detail, allowed transitions with the loaded version, notes, archive, conflict, not found).
- Playwright `leads.spec.ts`: published site → anonymous phone visitor sends a lesson request → teacher email → inbox → detail → status and note. E2E teachers are confirmed through the dev mailbox (`teacherViaApi`).

## Public site tests (R1-P4)

- Unit: `PublicSiteRulesTests` (SEO title/description/fallbacks, JSON-LD content and escaping, currency minor units and price formatting, mk/en text parity, palette CSS = `SitePalettes`, slug route constraint, page-view visitor filter, robots/sitemap output, **render-model allow-list snapshot**, Vite manifest resolution and missing build); `SiteRulesTests` also checks contrast on the page background.
- Integration: `PublicSiteTests` (404 + noindex for unknown/unpublished, complete HTML with meta tags and parsed JSON-LD, only public offers and visible sections, encoded XSS payloads incl. `</script>`, draft vs published and unpublish, audit entries, **cache hit proven by a change behind the app's back, then eviction by profile and offer changes**, signed-in requests bypass the cache, query strings share an entry, suspended owner hidden and back after reactivation, sitemap/robots, page views only for visitors and from cached hits, concurrent first views counted, public photo rules, publish validation and isolation, onboarding step, slug change moves the page, landing page in both languages, read-only public tenant binding); `SpaHostingTests` (`/` is the Razor landing page with the built stylesheet).
- Vitest: publish card (publish, address, views, version follow-up, unpublish confirmation, disabled before the first save, slug-move warning), `copy-link` island and `mountIsland`, fixed minor-unit table identical to `Currencies.MinorDigits` (never the browser's CLDR data), `/` hands over to the server.
- Playwright `public-site.spec.ts`: publish → anonymous **phone** visit (Pixel 7: heading, title, canonical, island, no horizontal scroll, no console/CSP errors) → unpublish → 404 with noindex; landing page links to registration.

## Site tests (R1-P3)

- Unit: `SiteRulesTests` (slug rules and reserved words, name-based suggestion with transliteration, WCAG AA contrast of every palette, section schema, normalization).
- Integration: `SiteTests` (suggestions, draft save and version conflicts, slug uniqueness, **concurrent claims of one slug with exactly one winner**, settings and section validation, preview: encoded HTML, noindex, hidden sections, invalid/tampered/expired tokens, isolation kit, onboarding step, every SPA prefix reserved).
- Vitest: `SiteEditorPage.test.tsx` (suggestions, save with order/visibility/items, sandboxed preview, live availability, browser validation, conflicts).

## Offer tests (R1-P2)

- Unit: `OfferRulesTests` (kind-specific fields, price, duration, mode, currency).
- Integration: `OfferTests` (all three kinds, default currency, ordering, validation, **CHECK constraints via raw SQL** bypassing the API, version conflicts, archive/inactive → not bookable through `IOfferLookup`, reorder, isolation kit, onboarding "first offer").
- Vitest: `OffersPage.test.tsx` (money parsing, list, create package, browser validation, edit with version and conflict, reorder, archive).

## Teacher profile tests (R1-P1)

- Unit: `ImageProcessorTests` (magic bytes, limits, disguised files, EXIF/comment removal and orientation with real JPEGs that carry injected metadata, built in memory by `TestImages`).
- Integration: `TeacherProfileTests` (catalog, profile CRUD and validation, photo storage and cleanup, upload rules, antiforgery, isolation kit, derived onboarding), `LocalDiskFileStorageTests` (traversal, no overwrite). Uploaded files go to a per-run temporary folder (`ClassHubApiFactory.FileRoot`); seeded catalog tables survive the Respawn reset.
- Playwright: `e2e/profile.spec.ts` completes the profile with a photo (a PNG generated in memory) on desktop and mobile and checks the checklist.

## Integration test database safety

Integration tests **never** use the local `ClassHubDb`, `.\SQLEXPRESS`, LocalDB, or any shared server. `ClassHubApiFixture` (one per test run, `[Collection(ApiCollection.Name)]`) does, in order:

1. `DockerEndpointGuard.AssertLocalEngine`: the Docker endpoint is `npipe://`, `unix://` or loopback TCP.
2. Starts `mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04` (override: `CLASSHUB_TEST_SQL_IMAGE`, must stay an `mcr.microsoft.com/mssql/server:*` image) with a random per-run `sa` password.
3. `DockerEndpointGuard.AssertLocalContainerEndpoint`: loopback host, random port (never 1433).
4. The server reports the container id as machine name and Linux as platform.
5. `TestDatabaseGuard.AssertIsolated`: data source must be exactly the container's `host,port`; database must be `ClassHubTest_<guid>`; Integrated Security, `.`, `(local)`, named instances, pipes, shared memory, LocalDB, `AttachDbFilename`, `ClassHubDb` and system databases are refused.
6. Builds the API host in the `Testing` environment (no `appsettings.Development.json`, no user-secrets), replaces the DbContext configuration with the container connection string, checks the guard again on the DbContext's real connection string, then applies the migrations.

If Docker is unavailable, the fixture fails with "Docker is required". The tests **never** fall back to another server. `Safety/TestDatabaseGuardTests` checks the guards without Docker; `Safety/DatabaseIsolationTests` checks from inside the host that it really uses the container database.

Helpers:
- `fixture.CreateClient()`: HTTP client for the in-memory API.
- `fixture.CreateDbContext()`: a context on the test database for arrange/assert steps.
- `fixture.CreateIsolatedDatabaseConnectionString()`: a fresh `ClassHubTest_*` database in the same container (for migration or convention tests).
- `fixture.ResetDatabaseAsync()`: Respawn reset (keeps migration history and seeded technical rows).

Tests create their own data with unique values and never depend on global row counts.

## What is covered (R0-P2)

| Area | Tests |
|---|---|
| Health | `/health`, `/health/live`, `/health/ready` return 200 without details |
| Database settings | collation `Macedonian_FYROM_100_CI_AS_SC`, `READ_COMMITTED_SNAPSHOT ON`, all migrations applied, no pending model changes |
| Migrations | apply → revert to empty → re-apply on a fresh database |
| Conventions | UTC round-trip (local → UTC), Cyrillic + supplementary characters, `datetime2(3)`, many NULLs in an optional unique column, duplicates rejected, no cascading delete |
| Safety | guards refuse every non-container target; host environment is `Testing`; the DbContext's database is the container's |

## What is covered (R0-P3)

Platform tests use `Platform/PlatformApiFactory`: the real pipeline in the `Testing` environment with an unreachable connection string (no Docker; no test calls a database endpoint). Probe endpoints under `/api/_probe` (test-only `IModule`) exercise the pipeline.

| Area | Tests |
|---|---|
| ProblemDetails | expected errors keep status, `code`, safe title, `traceId`; unexpected errors are a generic 500 without exception text or stack; malformed JSON → 400 `BAD_REQUEST` |
| Validation | invalid body → 400 `VALIDATION_FAILED` with camelCase paths (`items[0].displayName`); valid body reaches the handler |
| Authorization | protected, undeclared (fallback) and unknown routes → 401 problem for anonymous callers, no redirect; **every real endpoint declares AllowAnonymous or a named policy** (`EndpointAuthorizationMetadataTests`) |
| Rate limiting | the 4th request over a limit of 3 → 429 `RATE_LIMITED` + `Retry-After`; invalid limits stop the host at startup |
| Antiforgery | unsafe request without token or with a forged token → 400 `ANTIFORGERY_INVALID`; cookie + header → accepted; safe methods pass; token cookies have the right flags |
| Security headers | API: deny-all CSP, `DENY`, `nosniff`, referrer and COOP; pages: same-origin CSP, `SAMEORIGIN` |
| Contract | `OpenApiContractTests`: committed `openapi.json` equals the API document |
| Logging (unit) | secrets redacted (top-level, nested, dictionaries), emails masked, `CancellationToken` untouched |
| Architecture | Core ↛ Api; modules use other modules only via `Contracts`; `Domain` ↛ ASP.NET/EF/Infrastructure; SharedKernel ↛ modules/infrastructure |

## What is covered (R0-P4)

`Identity/AuthFlowTests` (Testcontainers SQL Server, `ApiClient` with cookies + antiforgery header, `WaitForEmailAsync` reading the dev mailbox):

| Area | Tests |
|---|---|
| Registration | register → confirmation email (en and default mk) → confirm → sign in; existing email answers the same 202 and gets an "already registered" email; **6 parallel registrations of one email create exactly one account** (unique index); weak, common and email-based passwords rejected for new and known emails alike; invalid emails rejected |
| Sign-in | correct sign-in returns the user; wrong password and unknown email give identical 401s; 5 failures lock the account (even the right password then fails); cookie flags (HttpOnly, SameSite=Lax, path, persistent only with remember-me); antiforgery header required on auth POSTs |
| Sessions | `/me` requires a session; logout ends it (token issued at sign-in accepted); authenticated callers get 404 for unknown routes; **suspension ends existing sessions and blocks sign-in** (403 `ACCOUNT_SUSPENDED`) |
| Password reset | reset email → new password works, old password and **existing sessions stop working**, the link cannot be reused; unknown email → 202 and no email; forged reset/confirmation tokens → 400 `INVALID_TOKEN` |
| Storage | passwords stored only as Identity V3 hashes; delivered outbox rows keep no body |
| Dev mailbox | `/api/dev/mailbox` lists delivered messages in Testing |
| Unit | common-password rules, localized and HTML-encoded emails, outbox backoff, email normalization |

The Testing host validates security stamps on every request (`Identity:SecurityStampValidationIntervalSeconds=0`) and dispatches email every 100 ms.

## What is covered (R0-P5)

| Area | Tests |
|---|---|
| Provisioning | registration creates exactly one Owner workspace (localized name, MKD, Europe/Skopje) and an audit entry; a failing provisioning rolls back the account and its email |
| Settings | owner reads and updates name/time zone/currency/locale (audited with before/after); invalid IANA zones, currencies, locales and empty names are rejected |
| Isolation | another teacher gets 404 on GET and PATCH and changes nothing; a platform admin without membership gets 404; unknown and malformed ids 404; anonymous 401; tenant rows are invisible to a context without a tenant |
| Machinery (probe model) | filter shows only the current workspace and nothing without one; inserts stamped; foreign-workspace writes refused; WorkspaceId cannot change; orphan rows refused; **the database rejects cross-workspace references** (composite FK) |
| CLI | `grant-platform-owner`: unknown email → 1, first owner → 0, second owner needs `--force-add` (2 otherwise), audited, role change ends the old session, not reachable over HTTP; a failure before the audit entry grants nothing; a concurrent bootstrap waits for the lock and is refused once an owner exists |
| Migrations | `WorkspaceBackfillMigrationTests`: upgrading a populated R0-P4 database gives every existing account an owned workspace (account language and time zone), audited, once |
| Architecture | tenant-filter bypasses and raw SQL only in allow-listed files |
| Unit | role permissions, policy names, tenant context binding, unknown permissions |

`Infrastructure/TenantIsolationKit` provides `CreateTeacherAsync` (register, sign in, workspace id) and `AssertCrossTenantDeniedAsync` for every later phase.

## CI

See [DEVELOPMENT.md](DEVELOPMENT.md#required-checks-github-actions). `integration` runs these tests on the runner's Docker. `migrations` applies all migrations to an empty SQL Server 2022 container, re-runs the idempotent script, and checks collation and RCSI.
