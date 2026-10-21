using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace NAP.Core;

/// <summary>Process-local proofs of pure ledger validation. Never caches authority, filesystem
/// observations or returned model objects. Every lookup fingerprints all indexed fields and every
/// history row freshly under the caller's IO lease. A miss executes the original full validator.
/// Global accounting bounds all stores/universes together, including long scope strings.</summary>
// The reserved :database: item records complete schema/domain validation of an exact
// freshly hashed DB under its native IO lease and exact roots. Sharing it across store
// instances avoids revalidating identical bytes; no write pre-approves its own result.
internal static class LedgerValidationProof
{
    private const int MaximumEntries = 4096;
    private const long MaximumBytes = 8L * 1024 * 1024;
    private sealed record Entry((string Scope, string Item) Key, string Fingerprint, long Bytes);
    private static readonly object Gate = new();
    private static readonly Dictionary<(string Scope, string Item), LinkedListNode<Entry>> Entries = [];
    private static readonly LinkedList<Entry> Recent = [];
    private static long _bytes;

    internal static string Scope(string database, string roots) => database + "|" +
        Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(roots.AsSpan())));

    internal static bool Matches(string scope, string item, string fingerprint)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue((scope, item), out var entry) || entry.Value.Fingerprint != fingerprint) return false;
            Recent.Remove(entry); Recent.AddLast(entry); return true;
        }
    }
    internal static void Remember(string scope, string item, string fingerprint)
    {
        var key = (scope, item); var bytes = checked(512L + 2L * (scope.Length + item.Length + fingerprint.Length));
        lock (Gate)
        {
            if (Entries.Remove(key, out var prior)) { Recent.Remove(prior); _bytes -= prior.Value.Bytes; }
            if (bytes > MaximumBytes) return;
            while (Entries.Count >= MaximumEntries || _bytes + bytes > MaximumBytes)
            {
                var first = Recent.First!; Entries.Remove(first.Value.Key); Recent.RemoveFirst(); _bytes -= first.Value.Bytes;
            }
            Entries.Add(key, Recent.AddLast(new Entry(key, fingerprint, bytes))); _bytes += bytes;
        }
    }

    internal static string ReadFingerprint(SqliteConnection db, SqliteDataReader workflow)
    {
        using var fingerprint = Begin(workflow);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT revision,payload,hash,previous_hash FROM history WHERE item=$i ORDER BY revision";
        command.Parameters.AddWithValue("$i", workflow.GetString(0));
        using var history = command.ExecuteReader(); while (history.Read()) fingerprint.History(history);
        return fingerprint.Finish();
    }
    internal static Fingerprint Begin(SqliteDataReader workflow) => new(workflow);

    internal sealed class Fingerprint : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private int _history;
        internal Fingerprint(SqliteDataReader workflow)
        {
            try
            {
                Text(workflow.GetString(0)); Text(workflow.GetString(1)); Text(workflow.GetString(2));
                Number(workflow.GetInt64(3)); Text(workflow.GetString(4)); Text(workflow.GetString(5));
            }
            catch { _hash.Dispose(); throw; }
        }
        internal void History(SqliteDataReader row)
        {
            if (++_history > 1025) throw AutomationValidation.Corrupt("Ledger history exceeds bounds.");
            Number(row.GetInt64(0)); Text(row.GetString(1)); Text(row.GetString(2));
            Text(row.IsDBNull(3) ? null : row.GetString(3));
        }
        private void Text(string? value)
        {
            if (value?.Length > 2 * 1024 * 1024) throw AutomationValidation.Corrupt("Ledger proof input exceeds bounds.");
            Number(value?.Length ?? -1);
            // Exact UTF-16 code units, including surrogate boundaries; no normalization or
            // replacement encoding can equate distinct raw payloads. Proofs never leave this process.
            if (value is not null) _hash.AppendData(MemoryMarshal.AsBytes(value.AsSpan()));
        }
        private void Number(long value)
        { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(bytes, value); _hash.AppendData(bytes); }
        internal string Finish() { Number(_history); return Convert.ToHexString(_hash.GetHashAndReset()); }
        public void Dispose() => _hash.Dispose();
    }
}
