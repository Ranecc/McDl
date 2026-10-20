using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace McDl.Core
{
    /// <summary>Unpacks native jars into the version directory, honouring the extract rules.</summary>
    public static class NativesUnpacker
    {
        /// <summary>Patterns every extraction starts from: archive bookkeeping, no game payload.</summary>
        public static readonly List<string> DefaultExcludes = new List<string>
        {
            "META-INF/",
            // Natives jars carry checksum bookkeeping next to the dlls; a game does not need it.
            "*.git",
            "*.sha1",
            "*.md"
        };

        public static void ExtractAll(IEnumerable<NativesExtraction> extractions, CancellationToken ct)
        {
            if (extractions == null) return;

            var materialized = new List<NativesExtraction>();
            foreach (var extraction in extractions) if (extraction != null) materialized.Add(extraction);

            // A previous run may have left dlls behind (another architecture, an older version of the
            // library). They share file names with the fresh ones, so the folder is rebuilt from scratch.
            ClearTargetDirectories(materialized, ct);

            foreach (var extraction in materialized)
            {
                ct.ThrowIfCancellationRequested();
                if (extraction.Archive?.TargetPath == null) continue;

                var archivePath = extraction.Archive.TargetPath;
                if (!File.Exists(archivePath)) continue;

                var targetDir = extraction.TargetDir;
                if (string.IsNullOrEmpty(targetDir)) continue;
                Directory.CreateDirectory(targetDir);

                var excludes = extraction.Excludes ?? DefaultExcludes;

                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        if (IsExcluded(entry.FullName, excludes)) continue;

                        var fileName = Path.GetFileName(entry.FullName);
                        if (string.IsNullOrEmpty(fileName)) continue;

                        var destination = Path.Combine(targetDir, fileName);
                        try
                        {
                            if (File.Exists(destination)) File.Delete(destination);
                            entry.ExtractToFile(destination, true);
                        }
                        catch (IOException)
                        {
                            // Some natives ship the same dll twice (32/64 bit); keep the last one.
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }
                }
            }
        }

        private static void ClearTargetDirectories(IList<NativesExtraction> extractions, CancellationToken ct)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var extraction in extractions)
            {
                ct.ThrowIfCancellationRequested();

                var dir = extraction.TargetDir;
                if (string.IsNullOrEmpty(dir) || !seen.Add(dir)) continue;
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.GetFiles(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                        // A file locked by a running game keeps its old copy; nothing else to do.
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        private static bool IsExcluded(string entryName, IList<string> excludes)
        {
            foreach (var pattern in excludes)
            {
                if (string.IsNullOrEmpty(pattern)) continue;

                if (pattern.EndsWith("/", StringComparison.Ordinal))
                {
                    if (entryName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (pattern.StartsWith("*.", StringComparison.Ordinal))
                {
                    if (entryName.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (string.Equals(Path.GetFileName(entryName), pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
