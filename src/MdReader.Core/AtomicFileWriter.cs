namespace MdReader.Core;

/// <summary>
/// Atomic file writes: content lands in a same-directory temporary file first,
/// then replaces the target in one operation, so a crash or power loss mid-save
/// can never leave a truncated document. Byte-level fidelity is the caller's
/// job (see <see cref="TextFileIO"/>); this class only makes the write safe.
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/> atomically.
    /// The temp file lives in the target's directory (same volume — rename is
    /// atomic there; a cross-volume temp would silently degrade to copy+delete).
    /// On failure the temp file is removed and the original is untouched.
    /// </summary>
    public static void Write(string path, byte[] bytes)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException($"Cannot resolve directory for '{path}'.");

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(tempPath, bytes);

            if (File.Exists(fullPath))
            {
                try
                {
                    // Preserves the target's identity, ACLs, and (where the
                    // filesystem supports it) alternate streams.
                    File.Replace(tempPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException or IOException)
                {
                    // FAT/exFAT and some network volumes lack ReplaceFile
                    // semantics; Move-with-overwrite is the honest fallback
                    // (still a single rename on NTFS-like volumes). When that
                    // also fails the target is locked or read-only — surface
                    // one actionable error naming the file.
                    try
                    {
                        File.Move(tempPath, fullPath, overwrite: true);
                    }
                    catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
                    {
                        throw new IOException(
                            $"Could not save '{Path.GetFileName(fullPath)}': the file is locked by another program or not writable.",
                            moveEx);
                    }
                }
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best-effort; the caller's original exception matters more.
        }
    }
}
