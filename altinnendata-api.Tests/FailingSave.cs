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
