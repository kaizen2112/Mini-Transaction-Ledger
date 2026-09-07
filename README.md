# Mini Transaction Ledger

A personal financial ledger: accounts, credits and debits, atomic transfers,
reversals, and reports — built to be explained line by line rather than to
ship fast.

**Stack:** ASP.NET Core 9 Web API · Entity Framework Core 8 · PostgreSQL 16 ·
Next.js 16 + TypeScript · xUnit + Testcontainers

---

## What it does

- **Accounts** — cash, savings, business, wallet. Every account starts at
  `0.00` and is funded by recording a credit.
- **Transactions** — credits and debits against one account, categorised,
  searchable, filterable, paginated in SQL.
- **Transfers** — money moved between two of your own accounts in a single
  database transaction: both balances change, or neither does.
- **Reversals** — the only way to correct a mistake. A reversal is a new
  compensating entry; the original row is never edited or deleted.
- **Reports** — balance, credits/debits/net, spending by category, and a
  month-by-month comparison, all aggregated by PostgreSQL.
- **Audit log** — every balance-affecting action is recorded in the same
  transaction as the action itself.

Two properties are load-bearing and worth stating up front:

- **The ledger is append-only.** There is no `PUT` and no `DELETE` anywhere in
  the API. History cannot be rewritten, only added to.
- **Money-moving requests are idempotent.** `POST /api/transactions` and
  `POST /api/transfers` require an `Idempotency-Key`; a retried request
  replays the original response instead of moving the money twice.

---

## Running it

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) — for
  PostgreSQL, and for the integration tests

### 1. Configuration

```bash
cp .env.example .env
```

Then edit `.env` and set two real values:

| Key | What it needs |
|---|---|
| `POSTGRES_PASSWORD` | any local password |
| `JWT_KEY` | at least 32 characters of random text |

`.env` is gitignored and never committed. The API validates both at startup
and refuses to boot if either is missing, rather than failing later on the
first login.

### 2. Start PostgreSQL

```bash
docker compose up db
```

Postgres listens on `localhost:5432`. The port is published so you can inspect
the schema with `psql` or pgAdmin.

### 3. Start the API

```powershell
./run-api.ps1
```

The API comes up on **http://localhost:8080**, applies any pending migrations,
and serves Swagger at http://localhost:8080/swagger.

The script exists because `dotnet run` on its own has no database connection
string or signing key — it reads `.env` and passes the same environment
variables Docker Compose would. It is not a separate mechanism, just the same
one without the containers.

### 4. Start the frontend

```bash
cd frontend
npm install     # first time only
npm run dev
```

Open **http://localhost:3000**, register an account, and sign in.

---

## Trying it out

1. **Register** at `/register`, then sign in.
2. **Create two accounts** — say `Cash` and `Savings`. Both start at `0.00`.
3. **Fund one**: Transactions → *Add transaction* → Credit, `5000`, Salary.
4. **Spend from it**: another transaction, Debit, `320`, Food.
5. **Try to overspend**: a debit larger than the balance is refused with the
   current balance quoted back, and nothing changes.
6. **Transfer** `1000` from Cash to Savings — both balances move together.
7. **Reverse** the `320` debit. A compensating credit appears, the original is
   badged *Reversed*, and its amount and description are untouched.
8. **Look at Reports**: the transfer and the reversed purchase are absent from
   *spending by category*, but present in the summary totals — spending and
   ledger activity are different questions.

---

## Tests

```bash
cd backend
dotnet test
```

140 tests. The integration tests run against a real PostgreSQL 16 container via
Testcontainers rather than an in-memory provider, so row locking, unique
indexes, and check constraints are exercised as they actually behave. **Docker
must be running.**

```bash
cd frontend
npx tsc --noEmit    # types
npx eslint .        # lint
npm run build       # production build
```

---

## Layout

```
backend/
  TransactionLedger/          Controllers/ Services/ Domain/ DTOs/ Data/ Middleware/
  TransactionLedger.Tests/    Unit/ Integration/ Infrastructure/
frontend/
  app/                        routes: /login /register / /accounts /transactions /transfers /reports
  components/                 ui primitives, feature components, hand-rolled charts
  lib/                        api client, auth/session, formatting, hooks
  types/                      hand-written mirrors of the backend DTOs
docs/                         requirements, business rules, schema, API contract, ADRs
```

A modular monolith, organised by folder rather than split into layered
projects: one API project and one test project. Nothing in this scope justifies
more, and the simpler structure is easier to defend.

---

## Notable design decisions

| Decision | Why |
|---|---|
| Balances stored, not recomputed | A running `SUM` over every historical row gets slower forever. The stored balance is protected by a `SELECT … FOR UPDATE` row lock inside an explicit transaction. |
| Transfers lock accounts in ascending id order | Two simultaneous opposite transfers would otherwise deadlock. A deterministic lock order makes that impossible. |
| Corrections are compensating entries | An edited or deleted transaction destroys the audit trail. A reversal adds a row and leaves history intact. |
| Uniqueness enforced by index, not pre-check | A `SELECT`-then-`INSERT` has a race between the two statements. The unique index cannot lose that race; the violation is translated into a `409`. |
| No repository layer, no AutoMapper, no MediatR | EF Core's `DbSet` is already a repository. Each additional abstraction here would add indirection without removing duplication. |
| `decimal` / `numeric(18,2)` for money | Binary floating point cannot represent `0.10` exactly, and money is not an approximation. |
| Errors carry a stable `code` | The frontend switches on `code`, never on human-readable text, so wording can change without breaking clients. |

Full rationale, including the rejected alternatives, is in `docs/`.

---

## API

Swagger UI at http://localhost:8080/swagger while the API is running in
Development. The **Authorize** button accepts a bearer token from
`POST /api/auth/login`, so every protected endpoint can be exercised from the
browser.

| Method | Path | Idempotency |
|---|---|---|
| `POST` | `/api/auth/register`, `/api/auth/login` | — |
| `GET` `POST` | `/api/accounts` | — |
| `GET` | `/api/accounts/{id}`, `/api/accounts/{id}/balance` | — |
| `GET` `POST` | `/api/accounts/{id}/transactions` | **required on POST** |
| `GET` | `/api/transactions/{id}` | — |
| `POST` | `/api/transactions/{id}/reverse` | by construction |
| `GET` `POST` | `/api/transfers` | **required on POST** |
| `GET` | `/api/reports/{summary,categories,monthly}` | — |

---

