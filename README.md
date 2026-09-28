# UniflowApi — .NET port of Nyanjigi, multi-tenant on UniflowDB

## What this is
A thin ASP.NET Core Web API where every mutation is a `StoredProcExecutor` call
into UniflowDB — no EF Core, no LINQ-to-SQL, no ad-hoc SQL text anywhere in the
C# code. The database enforces ACID via `BEGIN TRAN/COMMIT/THROW` inside each
proc; the API's job is auth, tenant scoping, request/response shaping, and
nothing else.

## Response contract (unchanged from Nyanjigi)
Every endpoint returns `{ success, message, data, errors, timestamp }` — same
shape your existing frontend already expects (`Common/ApiResponse.cs`).

## Auth
- JWT bearer, same `type: admin|customer` claim Nyanjigi used, plus a new
  `scheme_id` claim (Nyanjigi was single-tenant, so it never needed one).
- `ICurrentUser` is the *only* place `SchemeId` is read from — controllers
  never trust a `SchemeId` from the request body/query string. This is what
  keeps one scheme's admin from touching another scheme's data.
- Passwords are bcrypt (`BCrypt.Net-Next`), verified in C#, matching Nyanjigi's
  `AuthUtils.comparePassword` — the hash never leaves the API as plaintext and
  T-SQL never does the comparison.

## SQL addenda, in run order
1. `UniflowDB_Full_Schema.sql` — core schema/procs.
2. `UniflowDB_Auth_Additions.sql` — LastLogin, Customers.PasswordHash, login procs.
3. `UniflowDB_Read_Procs.sql` — listing/detail procs.
4. `UniflowDB_Platform_Additions.sql` — SuperAdmins + `usp_ProvisionScheme` (new-tenant onboarding).
5. `UniflowDB_Equity_Additions.sql` — per-scheme Equity biller credentials, transaction dedup ledger, scoped customer lookup.
6. `UniflowDB_Contributions_MeterReadings_Procs.sql` — contribution generation/listing, meter-reading listing/due-report.
7. `UniflowDB_Extended_Procs_Part1.sql` — CustomerAdjustments (manual balance adjustments), profiles, password changes, customer admin actions (update/toggle-status/reset-password), stats.
8. `UniflowDB_Extended_Procs_Part2.sql` — Bills admin depth (get/update-status/bulk-status/delete/stats/overdue/summary), Payments admin depth, Fines status update, Receipts.
9. `UniflowDB_Extended_Procs_Part3_Settings.sql` — generic per-scheme Settings (key/value) system, notification history.
10. `UniflowDB_Extended_Procs_Part4_Admin.sql` — dashboard analytics, revenue trend, financial summary, outstanding customers, exports, activity log, system health, batch meter readings.

All parts are `CREATE OR ALTER` / `IF NOT EXISTS`-guarded — safe to re-run any of them at any time, in any order relative to each other (only the dependency on Part 1 for `usp_internal_QueueNotification`/`usp_internal_WriteBalanceAudit` from the core schema matters, and those already exist by the time you reach Part 1).

## Read `MULTI_SCHEME_GUIDE.md` next
That's the actual walkthrough: the one tenant-isolation rule everything
follows, the four identity types (customer/admin/equity_biller/superadmin),
how to provision a new scheme, the four-step checklist for adding any new
module, and how to test tenant isolation before shipping anything.

## Endpoint map (Nyanjigi → UniflowApi)

| Nyanjigi route | UniflowApi route | Backing proc |
|---|---|---|
| `POST /auth/admin/login` (not present in Nyanjigi — single admin) | `POST /api/v1/auth/admin/login` | `usp_GetAdminForLogin` + `usp_TouchAdminLastLogin` |
| `POST /customers/login` (implicit) | `POST /api/v1/auth/customer/login` | `usp_GetCustomerForLogin` + `usp_TouchCustomerLastLogin` |
| `POST /customers` | `POST /api/v1/customers` | `usp_CreateCustomer` |
| `GET /customers/:id` | `GET /api/v1/customers/{id}` | `usp_GetCustomerById` |
| `GET /customers` | `GET /api/v1/customers` | `usp_ListCustomersByScheme` |
| `GET /customers/:id/account-summary` | `GET /api/v1/customers/{id}/account-summary` | `usp_GetCustomerAccountSummary` |
| `GET /customers/:id/bills` | `GET /api/v1/bills/customer/{id}` | `usp_ListBillsByCustomer` |
| (admin bill-run) | `POST /api/v1/bills/generate` | `usp_GenerateMonthlyBills` |
| `GET /bills` | `GET /api/v1/bills` | `usp_ListBillsByScheme` |
| (admin marks fine) | `POST /api/v1/fines` | `usp_ApplyFine` |
| (overdue status refresh) | `POST /api/v1/bills/mark-overdue` | `usp_MarkOverdueBills` |
| `GET /customers/:id/fines` | `GET /api/v1/fines/customer/{id}` | `usp_ListFinesByCustomer` |
| `POST /payments` (via Equity STK callback) | `POST /api/v1/equity/callback` | `usp_RecordEquityTransaction` + `usp_ProcessPayment` |
| `GET /customers/:id/payments` | `GET /api/v1/payments/customer/{id}` | `usp_ListPaymentsByCustomer` |
| (n/a — Nyanjigi is single-tenant) | `POST /api/v1/platform/schemes` | `usp_ProvisionScheme` |
| (n/a) | `PUT /api/v1/admin/equity-credentials` | `usp_UpsertEquityBillerCredentials` |
| `POST /equity/auth/token` | `POST /api/v1/equity/login` | `usp_GetEquityBillerCredentials` |
| `POST /equity/validate-customer` | `POST /api/v1/equity/validate-customer` | `usp_FindCustomerForEquity` + `usp_GetCustomerOutstandingBalance` |
| (n/a) | `POST /api/v1/notifications` | `usp_QueueNotification` |
| (n/a) | `GET /api/v1/notifications/pending` | `usp_GetPendingNotifications` |
| (n/a) | `GET /api/v1/system/error-codes` | `usp_ListErrorCodes` |

## Deliberately built this pass
Auth (+ profile/password/validate/logout/dashboard), Customers (+ update/
toggle-status/reset-password/adjust-balance/stats/zone-analytics), Bills
(+ stats/overdue/summary/status updates/bulk status/delete), Payments
(+ admin ledger/stats/lookup), Fines (+ status update/listing), Contributions
(full CRUD + overdue/summary), Meter Readings (single + batch), Equity
(per-scheme biller model), Platform (SuperAdmin scheme provisioning),
Notifications (admin-queued + direct-send + dispatch-pending + history),
Settings (generic per-scheme key/value config), Receipts (derived from
Payments, no separate table), Admin analytics (dashboard, revenue trend,
financial summary, outstanding customers, activity log, system health),
and SMS (Africa's Talking, direct HTTP integration — no Node SDK needed).

## Still not ported
- **PDF receipt rendering** — `usp_GetReceiptById`/`usp_ListReceiptsByCustomer`
  return the receipt's data; turning that into an actual PDF is a separate
  library choice (QuestPDF is the usual .NET pick) and hasn't been wired up.
- **SMS delivery-report webhook** — `GET /admin/sms/delivery-status/:messageId`
  is a stub. Africa's Talking reports delivery asynchronously via a callback
  URL you register with them, not a pollable-by-ID endpoint — that callback
  receiver doesn't exist yet.
- **`settings/validation`, `settings/logs/security`, `settings/maintenance/run`,
  `settings/notifications/test`** — narrower admin-tooling endpoints from the
  Nyanjigi blueprint; the core settings CRUD (get/bulk-update/reset/category
  routes) is done, these are smaller follow-ons on the same pattern.

## Known parity gap flagged, not silently patched
Nyanjigi's `bills` table has a `previous_balance` column — it's unused by any
insert in the codebase I read, and its presence next to `total_amount` is
exactly the kind of column that invites arrears-in-bill compounding later. It
was deliberately **not** carried over into UniflowDB; a bill's `TotalAmount`
is only ever that period's charge (see design note 5 in the schema header).
