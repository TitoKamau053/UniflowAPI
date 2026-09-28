# Building UniflowApi for Multiple Schemes — a working guide

This is the guide you asked for: how the multi-tenant pieces fit together, and
the pattern to follow when you (or anyone else) adds the next endpoint or the
next scheme. Everything here refers to code already in the zip.

## 1. The one rule everything else follows

**SchemeID is never read from anything the caller controls.** Not the URL,
not the query string, not the request body. It comes from exactly one place:
the JWT claim, via `ICurrentUser.SchemeId` (`Services/ICurrentUser.cs`).

That's the entire multi-tenancy model. There's no `WHERE SchemeID = @X` a
developer could forget to add on some new endpoint and accidentally leak
cross-tenant data — because `@X` is never a parameter the caller supplies in
the first place. Every proc that touches tenant data takes `@SchemeID` as an
argument, and every controller gets that argument from `_me.SchemeId`, full
stop.

```csharp
// This is the ONLY correct pattern for a scheme-scoped read/write:
var bills = await _db.QueryAsync<BillRow>("usp_ListBillsByScheme",
    new { SchemeID = _me.SchemeId, ... });
```

If you ever find yourself writing `new { SchemeID = request.SchemeId, ... }`
— taking the scheme from the request instead of `_me` — stop. That's the bug
class this whole design exists to prevent.

## 2. Four identity types, four claim shapes

| `type` claim | Scoped to | Issued by | Can reach |
|---|---|---|---|
| `customer` | one Customer, one Scheme | `POST /auth/customer/login` | their own records only (`bills/customer/{id}` etc. check `_me.UserId == customerId`) |
| `admin` | one Scheme | `POST /auth/admin/login` | everything under their own `SchemeID` |
| `equity_biller` | one Scheme | `POST /equity/login` (Equity calls this, not a person) | `validate-customer` / `callback`, scoped to their scheme, nothing else |
| `superadmin` | **no scheme** — sits above all of them | `POST /platform/login` | `PlatformController` only: provisioning and listing schemes |

Four separate `[Authorize(Policy = "...")]` policies enforce this
(`Program.cs`). A token for one type simply doesn't satisfy another policy —
there's no privilege escalation path between them, because they're not tiers
of the same role, they're different claims entirely.

## 3. Standing up a new scheme

This is a `PlatformController` call, nothing more:

```
POST /api/v1/platform/schemes
Authorization: Bearer <superadmin token>

{
  "schemeName": "Kiambu Water Users Association",
  "adminUsername": "kiambu_admin",
  "adminPassword": "...",
  "adminFullName": "Jane Wanjiru",
  "initialFlatRate": 350.00
}
```

`usp_ProvisionScheme` does the Scheme + first BillingRate + first Admin in one
transaction — there's no window where a scheme exists but has no rate
configured or no admin able to log in. That admin can now log in normally at
`/auth/admin/login` and everything downstream (customers, bills, payments)
just works, scoped to their new `SchemeID`, with zero other code changes.

**Before you expose this to a network:** the platform seed creates a
`platform_owner` SuperAdmin with a placeholder password hash
(`UniflowDB_Platform_Additions.sql`, bottom). Generate a real hash —
`BCrypt.Net.BCrypt.HashPassword("your-real-password")` in a throwaway
`dotnet fsi`/console line — and `UPDATE dbo.SuperAdmins SET PasswordHash = ...`
before this ever touches the internet.

## 4. Adding a new module (the checklist)

Every controller in this project follows the same four steps. Do them in
this order — writing the controller before the proc exists is how you end up
guessing at result shapes:

1. **Proc first.** Decide what it needs from the caller (`@SchemeID` always,
   plus whatever's specific to the operation) and what it returns. Mutations
   get `BEGIN TRAN/COMMIT/THROW`; reads don't need a transaction at all.
2. **Row DTO.** A plain C# class whose property names match the proc's
   output columns exactly (Dapper maps by name, case-insensitively). Put it
   in `Models/`.
3. **Controller action.** Pull `_me.SchemeId` (or `_me.UserId` for a
   customer's own-record check), call `_db.QueryAsync`/`ExecuteAsync`/
   `ExecuteWithOutputAsync`, wrap the result in `ApiResponse<T>.Ok(...)`.
4. **Policy.** `[Authorize(Policy = "AdminOnly")]` unless you have a specific
   reason it should be reachable by a customer or nobody at all (login,
   health check).

If step 1 tempts you to accept `@SchemeID` as a parameter from the request
body instead of trusting the caller's claim — see rule #1. There's no
exception to it, including "just for this one internal tool" endpoints.

## 5. Testing tenant isolation

Before shipping any new endpoint, run this by hand (or script it once and
reuse it — it's the same four calls every time):

1. Provision two schemes (`POST /platform/schemes` twice).
2. Log in as each scheme's admin.
3. Create a customer under Scheme A.
4. Using Scheme B's admin token, try to `GET` that customer by ID.

You should get **404, not 403.** `usp_GetCustomerById` filters by
`(CustomerID, SchemeID)` together, so a customer belonging to another scheme
doesn't exist as far as that query is concerned — it's not "found then
denied," it's genuinely not there. That's a stronger guarantee than a
permission check: there's no code path that even sees the other scheme's row
before deciding to reject it.

Do this for every new listing/detail endpoint you add. It takes two minutes
and it's the one test that actually matters for a multi-tenant system.

## 6. Where Equity fits into this

Equity is scoped exactly like a scheme's Admin, not like a platform-level
actor: each scheme's `EquityBillerCredentials` row is separate
(`UniflowDB_Equity_Additions.sql`), so logging in as Scheme A's biller yields
a token whose `scheme_id` claim is Scheme A's — `usp_FindCustomerForEquity`
takes that `@SchemeID` and simply cannot resolve a Scheme B customer, even if
two schemes' customers happened to share a phone number.

To onboard Equity for a new scheme: that scheme's Admin calls
`PUT /api/v1/admin/equity-credentials` with whatever username/password you've
agreed with Equity for that scheme's paybill, then hands Equity that
username/password (Equity calls `POST /equity/login` with it, from a
whitelisted IP, to get their own scoped token). No code changes, no per-scheme
deployment — it's all data.

## 7. Two things worth deciding before you go further

These aren't blocking, but they're real divergences from your actual Nyanjigi
production behavior that I didn't want to silently paper over:

- **Allocation order — resolved.** ~~The original UniflowDB spec said
  fines → bills → contributions; your live Nyanjigi code does
  bills → fines → contributions.~~ Confirmed: `usp_ProcessPayment` now
  allocates bills → fines → contributions, matching Nyanjigi.
- **Automatic notifications — resolved.** `usp_CreateCustomer` is now the
  only proc that auto-queues a notification (`account_created`); bill/fine/
  payment notifications go through the new admin-facing
  `POST /api/v1/notifications`. Fines are now exclusively admin-entered via
  `POST /api/v1/fines` (`usp_ApplyFine`) — there is no percentage-based
  auto-fine proc anymore. `POST /api/v1/bills/mark-overdue`
  (`usp_MarkOverdueBills`) still exists as a pure status refresh (no fine, no
  notification) so overdue reporting stays accurate.

## 8. Error codes: defined in the database, consumed structurally by the API

Every custom error any proc can raise is a row in `dbo.ErrorCodes` — number,
name, HTTP status, default message — and every proc raises it through
`usp_internal_RaiseError` rather than an inline `THROW 'some text'`. That
means the message a proc actually throws can never drift from what's
documented in the catalog; there's exactly one place either could be edited.

```sql
-- Registering a NEW error is always two steps, in this order:
INSERT INTO dbo.ErrorCodes (ErrorCode, ErrorName, HttpStatus, DefaultMessage)
VALUES (50014, 'DUPLICATE_ACCOUNT_NO', 400, N'That account number is already in use.');

-- ...then, inside whichever proc needs it:
IF EXISTS (...)
    EXEC dbo.usp_internal_RaiseError @ErrorCode = 50014;
```

The number itself carries the HTTP status, by range — no lookup needed on
the API's hot path:

| Range | HTTP status |
|---|---|
| 50000-50999 | 400 Bad Request |
| 51000-51999 | 404 Not Found |
| 52000-52999 | 401 Unauthorized |
| 53000-53999 | 403 Forbidden |
| 54000-54999 | 409 Conflict |

`StoredProcExecutor` (`Data/StoredProcExecutor.cs`) catches the `SqlException`,
reads `ex.Number` as the code and `ex.Message` as the already-resolved text,
maps the range to an HTTP status, and throws an `ApiException` carrying both.
The response envelope surfaces the code directly:

```json
{
  "success": false,
  "message": "AmountPaid must be greater than zero.",
  "errorCode": 50011,
  "timestamp": "2026-08-26T09:15:00.000Z"
}
```

`GET /api/v1/system/error-codes` (`usp_ListErrorCodes`) exists so a frontend
or API-docs page can render the whole catalog without you maintaining a
second copy of it in C#. If you add a new error code in the database, that
endpoint picks it up with no code change.

Anything below 50000 never reaches `ApiException` at all — those are
infrastructure failures (deadlock, timeout, a genuinely broken connection
string), get logged with `ILogger`, and surface as an ordinary 500. That
split is deliberate: a 50011 is a fact about the request ("you sent a
non-positive amount"); a deadlock is a fact about the database's current
load, and conflating the two would make client-side error handling guess
which kind of "error" it's looking at.
