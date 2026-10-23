using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using McDl.Core;
using Xunit;

namespace McDl.Tests
{
    public sealed class InstallVerifierTests : IDisposable
    {
        private readonly string _root;

        public InstallVerifierTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mcdl-verify-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            catch (IOException)
            {
                // 校验器自己的句柄应该都关了；删不掉就留给系统清理临时目录。
            }
        }

        private string WriteFile(string name, string content)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        private static long SizeOf(string content) => Encoding.UTF8.GetByteCount(content);

        private static string Sha1Of(string content)
        {
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
                var text = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }

        [Fact]
        public async Task MatchingFilesAreClean()
        {
            var path = WriteFile("client.jar", "client");

            var report = await InstallVerifier.VerifyAsync(new[]
            {
                new FileTask
                {
                    Label = "client.jar",
                    TargetPath = path,
                    Sha1 = Sha1Of("client"),
                    Size = SizeOf("client")
                }
            }, null, CancellationToken.None);

            Assert.True(report.Clean);
            Assert.Equal(1, report.Checked);
            Assert.Equal(1, report.Ok);
            Assert.Equal(0, report.Missing);
            Assert.Equal(0, report.Mismatched);
            Assert.Empty(report.Problems);
            Assert.Equal("校验 1 个文件：全部一致", report.Describe());
        }

        [Fact]
        public async Task Sha1ComparisonIgnoresCase()
        {
            var path = WriteFile("lib.jar", "lib");

            var report = await InstallVerifier.VerifyAsync(new[]
            {
                new FileTask
                {
                    Label = "lib.jar",
                    TargetPath = path,
                    Sha1 = Sha1Of("lib").ToUpperInvariant(),
                    Size = SizeOf("lib")
                }
            }, null, CancellationToken.None);

            Assert.True(report.Clean);
        }

        [Fact]
        public async Task MissingFileIsReported()
        {
            var path = Path.Combine(_root, "absent.jar");

            var report = await InstallVerifier.VerifyAsync(new[]
            {
                new FileTask { Label = "absent.jar", TargetPath = path, Sha1 = Sha1Of("x"), Size = 7 }
            }, null, CancellationToken.None);

            Assert.False(report.Clean);
            Assert.Equal(1, report.Checked);
            Assert.Equal(1, report.Missing);
            Assert.Equal(0, report.Ok);

            var problem = Assert.Single(report.Problems);
            Assert.Equal("absent.jar", problem.Label);
            Assert.Equal(path, problem.Path);
            Assert.Equal(FileCheckStatus.Missing, problem.Status);
            Assert.Equal(7, problem.ExpectedSize);
            Assert.Equal(0, problem.ActualSize);
            Assert.Equal("校验 1 个文件：缺失 1 个", report.Describe());
        }

        [Fact]
        public async Task ChangedContentWithTheSameSizeIsMismatch()
        {
            var path = WriteFile("tampered.jar", "bbbb");

            var report = await InstallVerifier.VerifyAsync(new[]
            {
                new FileTask
                {
                    Label = "tampered.jar",
                    TargetPath = path,
                    Sha1 = Sha1Of("aaaa"),
                    Size = SizeOf("bbbb")
                }
            }, null, CancellationToken.None);

            Assert.False(report.Clean);
            Assert.Equal(1, report.Mismatched);
            Assert.Equal(0, report.Missing);

            var problem = Assert.Single(report.Problems);
            Assert.Equal(FileCheckStatus.Mismatch, problem.Status);
            Assert.Equal(problem.ExpectedSize, problem.ActualSize);
        }

        [Fact]
        public async Task DifferentSizeIsMismatchAndTheHashIsNotConsulted()
        {
            // 只有 4 个字节的文件，却被声明成 4096；即便给的哈希是错的，结论也只能是大小不符。
            var path = WriteFile("short.jar", "abcd");

            var report = await InstallVerifier.VerifyAsync(new[]
            {
                new FileTask
                {
                    Label = "short.jar",
                    TargetPath = path,
                    Sha1 = "0000000000000000000000000000000000000000",
                    Size = 4096
                }
            }, null, CancellationToken.None);

            Assert.Equal(1, report.Mismatched);

            var problem = Assert.Single(report.Problems);
            Assert.Equal(FileCheckStatus.Mismatch, problem.Status);
            Assert.Equal(4096, problem.ExpectedSize);
            Assert.Equal(SizeOf("abcd"), problem.ActualSize);
        }

        [Fact]
        public async Task EntriesWithoutHashAreCheckedBySizeOnly()
        {
            var ok = WriteFile("no-hash.jar", "1234");
            var zeroSize = WriteFile("no-size.jar", "anything");

            var report = await InstallVerifier.VerifyAsync(new[]
            {
                new FileTask { Label = "no-hash.jar", TargetPath = ok, Size = SizeOf("1234") },
                new FileTask { Label = "no-size.jar", TargetPath = zeroSize }
            }, null, CancellationToken.None);

            Assert.True(report.Clean);
            Assert.Equal(2, report.Checked);

            // 计划给了大小但对不上，即使没有哈希也算不符。
            var mismatched = await InstallVerifier.VerifyAsync(new[]
            {
                new FileTask { Label = "no-hash.jar", TargetPath = ok, Size = 999 }
            }, null, CancellationToken.None);

            Assert.Equal(1, mismatched.Mismatched);
            Assert.Equal(0, mismatched.Ok);
        }

        [Fact]
        public async Task EmptyPlanIsClean()
        {
            var report = await InstallVerifier.VerifyAsync(new FileTask[0], null, CancellationToken.None)
                ;
            var nullReport = await InstallVerifier.VerifyAsync(null, null, CancellationToken.None)
                ;

            Assert.True(report.Clean);
            Assert.Equal(0, report.Checked);
            Assert.Empty(report.Problems);
            Assert.Equal("校验 0 个文件：全部一致", report.Describe());

            Assert.True(nullReport.Clean);
            Assert.Equal(0, nullReport.Checked);
        }

        [Fact]
        public async Task AlreadyCancelledTokenThrows()
        {
            var path = WriteFile("client.jar", "client");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InstallVerifier.VerifyAsync(new[]
                {
                    new FileTask { TargetPath = path, Sha1 = Sha1Of("client"), Size = SizeOf("client") }
                }, null, cts.Token));

                // 空计划也要立刻响应取消，否则调用方会把“没东西可查”当成校验通过。
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    InstallVerifier.VerifyAsync(new FileTask[0], null, cts.Token));
            }
        }

        [Fact]
        public async Task DuplicateTargetsAreCheckedOnce()
        {
            var path = WriteFile("dup.jar", "dup");

            var report = await InstallVerifier.VerifyAsync(new[]
            {
                new FileTask { Label = "dup.jar", TargetPath = path, Sha1 = Sha1Of("dup"), Size = SizeOf("dup") },
                new FileTask { Label = "dup.jar", TargetPath = path.ToUpperInvariant(), Sha1 = Sha1Of("dup"), Size = SizeOf("dup") }
            }, null, CancellationToken.None);

            Assert.Equal(1, report.Checked);
            Assert.Equal(1, report.Ok);
        }

        [Fact]
        public async Task ProgressReportsTheNumberOfCheckedFiles()
        {
            var one = WriteFile("a.jar", "a");
            var two = WriteFile("b.jar", "b");
            var three = WriteFile("c.jar", "c");
            var progress = new RecordingProgress();

            var report = await InstallVerifier.VerifyAsync(new[]
            {
                new FileTask { TargetPath = one, Size = SizeOf("a") },
                new FileTask { TargetPath = two, Size = SizeOf("b") },
                new FileTask { TargetPath = three, Size = SizeOf("c") }
            }, progress, CancellationToken.None);

            Assert.Equal(3, report.Checked);
            var values = progress.Values;
            // 每个文件报一次进度，四次并行也不该丢数或重复。
            Assert.Equal(3, values.Count);
            Assert.Equal(3, Max(values));
        }

        private static int Max(IReadOnlyList<int> values)
        {
            var max = 0;
            foreach (var value in values)
                if (value > max) max = value;
            return max;
        }

        private sealed class RecordingProgress : IProgress<int>
        {
            private readonly object _gate = new object();
            private readonly List<int> _values = new List<int>();

            public IReadOnlyList<int> Values
            {
                get
                {
                    lock (_gate) return new List<int>(_values);
                }
            }

            public void Report(int value)
            {
                lock (_gate) _values.Add(value);
            }
        }
    }
}
