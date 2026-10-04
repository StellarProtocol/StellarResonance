using System;
using System.IO.Abstractions;
using System.Security.Cryptography;

namespace StellarLauncher.Core.Dependencies;

/// <summary>Small sha256 helper shared by <see cref="DependencyService"/> and <see cref="DependencyParking"/>
/// so both "is this file still ours?" checks (I3) use one implementation.</summary>
internal static class DependencyFileHash
{
    public static string Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>True only if <paramref name="abs"/> exists and its current content hashes to
    /// <paramref name="recordedSha256"/> — the gate every destructive ledger-driven operation (remove,
    /// prune, park) must pass before touching a file, so a user- or tool-replaced file is left alone.</summary>
    public static bool Matches(IFileSystem fs, string abs, string recordedSha256) =>
        fs.File.Exists(abs) && string.Equals(Of(fs.File.ReadAllBytes(abs)), recordedSha256, StringComparison.OrdinalIgnoreCase);
}
