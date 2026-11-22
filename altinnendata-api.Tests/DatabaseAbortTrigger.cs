using Microsoft.EntityFrameworkCore;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

/// <summary>
/// Makes Postgres end UPDATEs of a table with a real deadlock error (40P01), as a lost lock cycle would. The first
/// <c>skip</c> UPDATEs go through, the next <c>times</c> fail. A sequence counts them, because a sequence is not
/// rolled back with the failed transaction.
/// </summary>
public static class DatabaseAbortTrigger
{
    public const int Always = int.MaxValue / 2;

    public static async Task InstallAsync(ApplicationDbContext db, string table, int times, CancellationToken ct, int skip = 0)
    {
        var sql = $"""
            CREATE SEQUENCE abort_count;
            CREATE FUNCTION abort_update() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE n bigint := nextval('abort_count');
            BEGIN
                IF n > {skip} AND n <= {skip} + {times} THEN
                    RAISE EXCEPTION 'deadlock detected' USING ERRCODE = '40P01';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER abort_update BEFORE UPDATE ON "{table}" FOR EACH ROW EXECUTE FUNCTION abort_update();
            """;
        await db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    /// <summary>The call failed with the database abort itself, whichever way the Npgsql version wraps it.</summary>
    public static async Task AssertAbortAsync(Func<Task> call)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(call);
        Assert.True(DatabaseAbort.Is(ex), $"Expected a database abort, got {ex}");
    }
}
