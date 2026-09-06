using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TransactionLedger.Data;
using TransactionLedger.Domain;
using TransactionLedger.DTOs;

namespace TransactionLedger.Services;

/// <summary>
/// The three reports of contract §7.
///
/// Every figure in this file is computed by PostgreSQL (BR-40, BR-43). Nothing
/// is materialised and summed in C#: a report over a year of transactions would
/// then drag every row across the wire to produce a single number, and it would
/// get slower every month the account stays open. The tests assert on the
/// generated SQL rather than only on the numbers, because an in-memory
/// implementation returns identical JSON.
/// </summary>
public sealed class ReportService : IReportService
{
    private readonly AppDbContext _dbContext;

    public ReportService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// BR-43. Transfer legs and reversals ARE included here: the summary
    /// describes what the ledger did, and a transfer really did debit one
    /// account and credit another. Only the category report, which claims to
    /// describe SPENDING, excludes them.
    /// </summary>
    public async Task<SummaryReportResponse> GetSummaryAsync(
        Guid userId,
        SummaryReportQuery query,
        CancellationToken cancellationToken)
    {
        query.Validate();

        var accounts = _dbContext.Accounts.AsNoTracking().Where(a => a.UserId == userId);

        // Deliberately NOT filtered by from/to: "what do I have" is a question
        // about now, not about the window (contract §7).
        var balances = await accounts
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalBalance = g.Sum(a => a.Balance),
                AccountCount = g.Count()
            })
            .SingleOrDefaultAsync(cancellationToken);

        // Filtered by from/to: these ARE questions about the window.
        var transactions = InRange(OwnedTransactions(userId), query.From, query.To);

        // One pass, two conditional sums, one count — a single aggregate query
        // rather than three round trips.
        var totals = await transactions
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Credits = g.Sum(t => t.Type == TransactionType.Credit ? t.Amount : decimal.Zero),
                Debits = g.Sum(t => t.Type == TransactionType.Debit ? t.Amount : decimal.Zero),
                Count = g.Count()
            })
            .SingleOrDefaultAsync(cancellationToken);

        // Null when the user has no rows at all: GroupBy over an empty set
        // produces no groups. Zero is the correct report, not an error.
        var credits = totals?.Credits ?? decimal.Zero;
        var debits = totals?.Debits ?? decimal.Zero;

        return new SummaryReportResponse(
            balances?.TotalBalance ?? decimal.Zero,
            credits,
            debits,
            credits - debits,
            totals?.Count ?? 0,
            balances?.AccountCount ?? 0,
            query.From,
            query.To);
    }

    /// <summary>
    /// BR-43, and the three exclusions are the whole point of this method.
    /// "You spent $320 on Food" has to mean money that actually left your
    /// hands, so the report drops:
    ///
    ///   1. credits — spending is debits only
    ///   2. transfer legs — moving money between your own accounts is not
    ///      spending, it is the same money in a different pocket
    ///   3. reversals AND the transactions they reverse — a refunded purchase
    ///      did not happen, and counting either half would be wrong: the
    ///      original would overstate spending, and the reversal is a credit
    ///      anyway
    ///
    /// Without these the report is confidently, silently wrong for anyone who
    /// uses transfers, which is everyone.
    /// </summary>
    public async Task<CategoryReportResponse> GetCategoriesAsync(
        Guid userId,
        CategoryReportQuery query,
        CancellationToken cancellationToken)
    {
        query.Validate();

        var owned = OwnedTransactions(userId);

        if (query.AccountId is { } accountId)
        {
            // BR-08: narrowing to an account that is not yours is a 404, not an
            // empty report — an empty report would confirm it exists.
            var ownsAccount = await _dbContext.Accounts
                .AsNoTracking()
                .AnyAsync(a => a.Id == accountId && a.UserId == userId, cancellationToken);

            if (!ownsAccount)
            {
                throw NotFound();
            }

            owned = owned.Where(t => t.AccountId == accountId);
        }

        var spending = InRange(owned, query.From, query.To)
            .Where(t => t.Type == TransactionType.Debit)
            .Where(t => t.TransferId == null)
            .Where(t => t.ReversesTransactionId == null)
            // EXISTS against the partial unique index UX_Transactions_Reverses.
            .Where(t => !_dbContext.Transactions.Any(r => r.ReversesTransactionId == t.Id));

        // GROUP BY "Category" in SQL. The ordering is by amount so the biggest
        // spending category is first, which is what a report is read for.
        //
        // OrderByDescending sits on the GROUP, not on the projected record: EF
        // cannot translate an ordering over a property of a constructor
        // projection ("could not be translated"), but it translates an ordering
        // over an aggregate of the group into ORDER BY SUM("Amount") DESC.
        var categories = await spending
            .GroupBy(t => t.Category)
            .OrderByDescending(g => g.Sum(t => t.Amount))
            .Select(g => new CategoryReportItem(
                g.Key,
                g.Sum(t => t.Amount),
                g.Count()))
            .ToListAsync(cancellationToken);

        // Summed from the already-materialised groups rather than a second
        // query: there is at most one row per category, so this is a handful of
        // decimals, not an unbounded set. BR-40 is about not paging or
        // aggregating LARGE sets in memory, and a second round trip here would
        // cost more than it saves.
        return new CategoryReportResponse(categories, categories.Sum(c => c.TotalAmount));
    }

    /// <summary>
    /// BR-43. Grouped by UTC calendar month. Like the summary and unlike the
    /// category report, transfer legs and reversals are included: this is a
    /// picture of ledger activity, not of spending.
    /// </summary>
    public async Task<MonthlyReportResponse> GetMonthlyAsync(
        Guid userId,
        MonthlyReportQuery query,
        CancellationToken cancellationToken)
    {
        query.Validate();

        var (from, to) = MonthlyWindow(query.Year);

        var months = await InRange(OwnedTransactions(userId), from, to)
            // Npgsql translates these to date_part('year'/'month', "OccurredAt"),
            // so the grouping happens in PostgreSQL (contract §7).
            .GroupBy(t => new { t.OccurredAt.Year, t.OccurredAt.Month })
            // Ordered on the group key rather than the projected record, for
            // the same translation reason as the category report above.
            .OrderBy(g => g.Key.Year)
            .ThenBy(g => g.Key.Month)
            .Select(g => new MonthlyReportItem(
                g.Key.Year,
                g.Key.Month,
                g.Sum(t => t.Type == TransactionType.Credit ? t.Amount : decimal.Zero),
                g.Sum(t => t.Type == TransactionType.Debit ? t.Amount : decimal.Zero),
                decimal.Zero))
            .ToListAsync(cancellationToken);

        // Net is computed after materialising because it is arithmetic on two
        // values already fetched, not an aggregate. Doing it in the projection
        // would mean a third SUM over the same rows for no benefit.
        return new MonthlyReportResponse(
            months.Select(m => m with { Net = m.Credits - m.Debits }).ToList());
    }

    /// <summary>
    /// A year means that calendar year; no year means the trailing 12 months
    /// (contract §7). The window starts at the first of the month 11 months ago
    /// so the result is 12 whole months, not 11 plus two partials.
    /// </summary>
    private static (DateTime From, DateTime To) MonthlyWindow(int? year)
    {
        if (year is { } value)
        {
            return (
                new DateTime(value, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(value, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        }

        var now = DateTime.UtcNow;
        var firstOfThisMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return (firstOfThisMonth.AddMonths(-11), now);
    }

    /// <summary>
    /// BR-07/BR-43: ownership as a join, in the predicate. Every report starts
    /// here, so no report can be written that forgets it.
    /// </summary>
    private IQueryable<Transaction> OwnedTransactions(Guid userId) =>
        from transaction in _dbContext.Transactions.AsNoTracking()
        join account in _dbContext.Accounts.AsNoTracking()
            on transaction.AccountId equals account.Id
        where account.UserId == userId
        select transaction;

    /// <summary>Inclusive UTC bounds on OccurredAt, matching history (BR-42).</summary>
    private static IQueryable<Transaction> InRange(
        IQueryable<Transaction> source,
        DateTime? from,
        DateTime? to)
    {
        if (from.HasValue)
        {
            source = source.Where(t => t.OccurredAt >= from.Value);
        }

        if (to.HasValue)
        {
            source = source.Where(t => t.OccurredAt <= to.Value);
        }

        return source;
    }

    private static DomainException NotFound() => new(
        ErrorCodes.NotFound,
        StatusCodes.Status404NotFound,
        "The requested resource was not found.");
}
