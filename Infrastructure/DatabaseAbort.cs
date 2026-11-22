using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace altinnendata_api.Infrastructure
{
    /// <summary>
    /// Postgres ended the transaction with a deadlock or a serialization failure. Nothing was saved and no other writer
    /// won, so the work can run again. It is never a sign that someone else changed the row. Npgsql 10 reports it from
    /// SaveChanges as a <see cref="DbUpdateException"/> inside an <see cref="InvalidOperationException"/>, and Npgsql 11
    /// as a <see cref="DbUpdateConcurrencyException"/>, so the Postgres error is looked for in the inner exceptions.
    /// </summary>
    public static class DatabaseAbort
    {
        public static bool Is(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure })
                    return true;
            }
            return false;
        }

        /// <summary>A save that failed because another writer changed the row first, or that Postgres aborted.</summary>
        public static bool IsConflictOrAbort(Exception ex) => ex is DbUpdateConcurrencyException || Is(ex);
    }
}
