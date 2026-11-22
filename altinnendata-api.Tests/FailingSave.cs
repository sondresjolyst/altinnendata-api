using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using altinnendata_api.Models;

namespace altinnendata_api.Tests;

/// <summary>Fails the save with the number <paramref name="failing"/>, counted from 1, the way a lost database connection would.</summary>
public sealed class FailingSave(int failing) : SaveChangesInterceptor
{
    private int _saves;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _saves) == failing)
            throw new DbUpdateException("The database connection was lost.");
        return ValueTask.FromResult(result);
    }

    /// <summary>A context on the database of <paramref name="existing"/> whose save number <paramref name="failing"/> fails.</summary>
    public static ApplicationDbContext On(ApplicationDbContext existing, int failing) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(existing.Database.GetConnectionString())
            .AddInterceptors(new FailingSave(failing))
            .Options);
}

/// <summary>
/// Fails the commit of a transaction. With <paramref name="afterTheServerCommitted"/> the commit has gone through and
/// only the answer is lost, as when the connection drops while the server says OK.
/// </summary>
public sealed class FailingCommit(bool afterTheServerCommitted) : DbTransactionInterceptor
{
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        System.Data.Common.DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
        afterTheServerCommitted ? ValueTask.FromResult(result) : throw new InvalidOperationException("The database connection was lost.");

    public override Task TransactionCommittedAsync(
        System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        afterTheServerCommitted ? throw new InvalidOperationException("The database connection was lost.") : Task.CompletedTask;

    /// <summary>A context on the database of <paramref name="existing"/> whose commits fail.</summary>
    public static ApplicationDbContext On(ApplicationDbContext existing, bool afterTheServerCommitted) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(existing.Database.GetConnectionString())
            .AddInterceptors(new FailingCommit(afterTheServerCommitted))
            .Options);
}
