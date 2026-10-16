# Approach

## Task 1: Defect fix and server-side filtering

### 1.1 DEFECT-117: reproducing the bug

I ran the app and watched the Network tab (filtered on `tickets`).

- On page 1, changing the Status dropdown to *In progress* sent no request to `GET /api/tickets`.
- Typing in the search box then sent a request that included the earlier status.
- On page 2 or later, changing Status did send a request.

The last point was the clue. The bug only appears when the current page is 1,
so it is not specific to the status dropdown. It depends on which filters the
fetching code reacts to.

### 1.1 Finding the cause

The filter bar (`TicketFilters.tsx`) and the page (`TicketListPage.tsx`) are
thin: they only call `updateFilters`. The fetching logic lives in
`hooks/useTicketList.ts`, where the fetch `useEffect` had this dependency array:

    [filters.search, filters.page, filters.pageSize]

Only those three fields could trigger a request. Status, priority, category,
customer, assigned agent, unassigned-only and sorting were used inside `load()`
but not watched. A lint-disable comment hid the warning from
`react-hooks/exhaustive-deps`.

`updateFilters` also resets `page` to 1 on every change. On page 1 that leaves
all watched values unchanged, so no request fires. On page 2+ the page change
fires one by accident. That is why the bug looked intermittent.

### 1.1 The fix

The fetch effect now depends on a single `queryKey`: the full filter set with
the search text replaced by a debounced, trimmed copy. Any filter change
changes the key and fetches immediately. Only search is debounced, so there
is no request per keystroke. A new filter added later (such as Task 2's
`slaStatus`) is included automatically because the key is built from the whole
filters object, so no extra wiring is needed.

Two other decisions:

- **Out-of-order responses.** The effect cleanup sets an `ignore` flag, so a slow
  response for an old query cannot overwrite the results of a newer one.
- **Loading state.** `isLoading` is derived by comparing the requested key with
  the key of the last settled response, instead of being set inside the effect.
  My first version called `setState` in the effect body, which the project's
  lint rule rejected because it causes an extra render. I fixed it by deriving
  the value, and did not disable the rule, since a suppressed lint rule is what
  hid the original bug.

Verified manually in the Network tab: changing Status, Priority or Category
sends one immediate request; typing in search sends one request after the
pause.

### 1.1 Regression test

`useTicketList.test.ts` already had a test for debounced search. I extended it,
keeping its style (fake timers, the shared `page` and `ticketListItem`
fixtures, `ticketsApi.getTickets` mocked) and asserting the request that is sent:

1. For each non-search filter (status, priority, category, customer, assigned
   agent, unassigned only), changing it on page 1 sends a request immediately,
   with no timers advanced past the search debounce. These six tests are the
   regression test for the defect.
2. Typing in search after choosing a status keeps the status in the request.
3. Three keystrokes in quick succession send one request for the final text,
   which covers the "no request per keystroke" acceptance criterion.

To prove the tests catch the defect, I stashed my fix and ran them against the
original hook. Result: `Tests  6 failed | 3 passed (9)`. The six failures are
the "requests immediately when <filter> changes" tests, which is the defect.
The other three pass on the original code, as expected: they guard behaviour
that must not regress, and are not specific to the defect. With the fix restored, 
all nine web tests pass, and the full `npm test` run
(API and web) passes.

Commit: "Fix DEFECT-117: ticket list ignored filter changes", containing only
the hook change and its tests, separate from the filtering work.

### 1.2 Reading the existing code

The API is layered. `TicketQuery` (an immutable record bound from the query
string) is defined in `Application/Contracts`, handlers depend on the
`ITicketQueries` abstraction, and the EF Core queries sit behind that
interface. Filtering therefore belongs in the `ITicketQueries`
implementation (`TicketQueries`), so the controller, the handler and the
contract did not need to change. `TicketQuery` already carried every filter the
web app sends; the API simply never applied them.

`UnassignedOnly` is a non-nullable `bool`, so `false` means "no filter" and only
`true` restricts the result, which matches FL-2.

### 1.2 Server-side filtering: design

`TicketQueries.GetPagedAsync` already built an `IQueryable` of tickets joined to
their customer, category and agent, then counted, sorted and paged it. I added
an `ApplyFilters` step that runs **before** the count:

    all tickets -> ApplyFilters -> CountAsync -> sort -> Skip/Take -> project

Filtering first means `totalCount`, `totalPages` and the pager describe the
filtered set (FL-5). Filtering in C# would load every ticket from SQL Server
into memory before narrowing it, which does not scale, and the count and
paging would be wrong.

`ApplyFilters` has one `if` block per filter, each adding a `Where`, so all
supplied filters combine with AND (FL-4). A filter that is not supplied adds
nothing.

Decisions:

- Exact matches (`status`, `priority`, `categoryId`, `customerId`,
  `assignedAgentId`) compare the ticket's own columns.
- `unassignedOnly=true` checks `AssignedAgentId == null` on the ticket rather
  than the joined agent, which gives simpler SQL.
- `search` is trimmed, blank is ignored, and it matches title, reference or
  customer name with `Contains`, translated to SQL `LIKE`. Case-insensitivity
  comes from SQL Server's default collation.
- `assignedAgentId` combined with `unassignedOnly=true` returns no rows, which
  follows the AND rule. I did not special-case it.

### 1.2 Adding the next filter

To add a filter such as Task 2's `slaStatus`: add the property to
`TicketQuery`, then add one `if` block to `ApplyFilters` in `TicketQueries`.
The count, sort and paging need no change because they all work from the
filtered query, and the frontend's query key already includes the whole
filters object (FL-6).

### 1.2 How I checked it

Manually, in Swagger against the seeded database:

- `priority=High&pageSize=5` returns 5 items, all High, while `totalCount` is the
  number of all High tickets. This shows the count is taken on the filtered set,
  not the page.
- `status` plus `priority` together return only tickets matching both.
- `search` matches customer name (`contoso`), reference (`TCK-0001`) and title.
- A blank search (`%20%20`) returns all 40 tickets.
- `unassignedOnly=true` returns only tickets with no assigned agent.
- `status=banana` returns 400 from model binding.

I also turned on EF Core command logging
(`Microsoft.EntityFrameworkCore.Database.Command`) and checked the SQL. For
`priority=High&pageSize=5`:

- `SELECT COUNT(*) ... WHERE [t].[Priority] = @priority`
- `SELECT ... WHERE [t].[Priority] = @priority ORDER BY [t].[CreatedAtUtc] DESC,
  [t].[Id] DESC OFFSET @p ROWS FETCH NEXT @p3 ROWS ONLY`

Both statements carry the same `WHERE`, so the count and the page are computed
on the filtered set, and `OFFSET/FETCH` is applied only after filtering. Values
are passed as parameters, not concatenated into the SQL.

For `search=contoso` the SQL was `WHERE [t].[Title] LIKE @search_contains
ESCAPE N'\' OR [t].[Reference] LIKE @search_contains0 ESCAPE N'\' OR [c].[Name]
LIKE @search_contains1 ESCAPE N'\'`. EF escapes `%` and `_` in the user's text,
so they match literally instead of acting as wildcards. The logging setting
was for checking only and is not committed.

Observations:

- The SLA status is calculated in C# after the rows are fetched, so it does not
  appear in the SQL. For Task 2's `slaStatus` filter the same rule has to be
  expressed as a SQL-translatable condition so that filtering, sorting, paging
  and counting all happen in the database.
- Scaling: `LIKE '%text%'` has a leading wildcard, so it cannot use an index
  and scans rows. That is acceptable for this data set. For millions of tickets
  I would move search to SQL Server full-text search or a dedicated search index.
- Known trade-off: the count query repeats the joins to customers, categories
  and agents even when no filter uses them. They are needed only for the
  customer-name search. I left it as it is because it is correct and the joins
  are on keys.

### 1.2 Tests

The unit-test project already referenced `Microsoft.EntityFrameworkCore.Sqlite`
but had no database tests, so I added one pattern: `TicketQueries.GetPagedAsync`
runs against the real `SupportDbContext` on SQLite in-memory
(`SqliteTestDatabase`), with each test seeding its own small data set.

- I chose SQLite over EF Core's InMemory provider because InMemory is not
  relational and does not translate the query to SQL, so it can pass queries a
  real database would reject.
- I chose SQLite over SQL Server in a container because the README promises
  that tests need no database. A container would need Docker for every run.
- Limit: SQLite is not SQL Server, and I hit that on my first run. My search
  test asserted that `contoso` matches the customer `Contoso`; it failed with 0
  results on SQLite, which compares text case-sensitively, while SQL Server's
  default collation is case-insensitive (and the same search matched in
  Swagger against SQL Server). I removed the case assertion from the SQLite
  test instead of changing the query to force case-insensitivity, because the
  query is right for the production database. The tests cover trimming and
  partial matching, and I verified case-insensitivity manually against SQL
  Server.

Tests (`TicketQueriesFilteringTests`): exact-match priority, several filters
combined with AND, customer-name search (exact, with surrounding spaces, and
partial), blank search ignored, and `totalCount`/`totalPages` computed on the
filtered set rather than the page. I extended `TicketBuilder` with
`WithTitle`, `ForCustomer` and `InCategory`; defaults are unchanged.

I confirmed the tests catch the missing filters by stashing `TicketQueries.cs`
and running them against the original query code. Result:
`Test summary: total: 33, failed: 6, succeeded: 27`. The six failures were the
priority, combined-filter, filtered-count and customer-search tests. The
blank-search test passed on the original code, as expected, since the old code
ignored search entirely; it guards behaviour that must not regress. With the
filters restored, all 33 tests pass.

Commit: "Apply ticket list filters in the database", separate from the defect
fix.