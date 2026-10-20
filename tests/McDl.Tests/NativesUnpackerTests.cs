using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using McDl.Core;
using Xunit;

namespace McDl.Tests
{
    public sealed class NativesUnpackerTests
    {
        private static string WriteJar(params string[] entryNames)
        {
            var path = Path.Combine(Path.GetTempPath(), "mcdl-natives-" + Guid.NewGuid().ToString("N") + ".jar");

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var name in entryNames)
                {
                    var entry = zip.CreateEntry(name);
                    using (var stream = entry.Open())
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        writer.Write("native");
                }
            }

            return path;
        }

        private static string NewDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "mcdl-out-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void DllsAreUnpackedAndBookkeepingIsSkipped()
        {
            var jar = WriteJar("lwjgl.dll", "lwjgl.dll.sha1", "lwjgl.dll.git", "META-INF/MANIFEST.MF");
            var target = NewDirectory();

            try
            {
                NativesUnpacker.ExtractAll(new[]
                {
                    new NativesExtraction
                    {
                        Archive = new FileTask { TargetPath = jar },
                        TargetDir = target,
                        Excludes = NativesUnpacker.DefaultExcludes
                    }
                }, CancellationToken.None);

                Assert.Equal(new[] { "lwjgl.dll" }, Array.ConvertAll(Directory.GetFiles(target), Path.GetFileName));
            }
            finally
            {
                File.Delete(jar);
                Directory.Delete(target, true);
            }
        }

        [Fact]
        public void StaleFilesFromAnEarlierRunAreRemoved()
        {
            var jar = WriteJar("lwjgl.dll");
            var target = NewDirectory();
            File.WriteAllText(Path.Combine(target, "lwjgl64.dll"), "old");

            try
            {
                NativesUnpacker.ExtractAll(new[]
                {
                    new NativesExtraction
                    {
                        Archive = new FileTask { TargetPath = jar },
                        TargetDir = target,
                        Excludes = NativesUnpacker.DefaultExcludes
                    }
                }, CancellationToken.None);

                Assert.Equal(new[] { "lwjgl.dll" }, Array.ConvertAll(Directory.GetFiles(target), Path.GetFileName));
            }
            finally
            {
                File.Delete(jar);
                Directory.Delete(target, true);
            }
        }

        [Fact]
        public void CancellationIsHonoured()
        {
            var jar = WriteJar("a.dll");
            var target = NewDirectory();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                try
                {
                    Assert.Throws<OperationCanceledException>(() => NativesUnpacker.ExtractAll(new[]
                    {
                        new NativesExtraction { Archive = new FileTask { TargetPath = jar }, TargetDir = target }
                    }, cts.Token));
                }
                finally
                {
                    File.Delete(jar);
                    Directory.Delete(target, true);
                }
            }
        }

        [Fact]
        public void NullAndEmptyInputsAreTolerated()
        {
            NativesUnpacker.ExtractAll(null, CancellationToken.None);
            NativesUnpacker.ExtractAll(new List<NativesExtraction>(), CancellationToken.None);
            NativesUnpacker.ExtractAll(new[] { new NativesExtraction() }, CancellationToken.None);
        }
    }
}
