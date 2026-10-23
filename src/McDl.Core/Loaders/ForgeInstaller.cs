using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>
    /// Forge 与 NeoForge：两者都提供官方 installer jar，内部自带处理器。
    /// 本类负责下载 installer，等游戏文件就绪后用本机 Java 跑 --installClient。
    /// 细节见 docs/loaders.md。
    /// </summary>
    public sealed class ForgeInstaller : ILoaderInstaller
    {
        private const string ForgeMavenBase = "https://maven.minecraftforge.net/";
        private const string NeoForgeMavenBase = "https://maven.neoforged.net/releases/";
        private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);
        private const int TailLines = 20;

        public ForgeInstaller(LoaderKind kind)
        {
            if (kind != LoaderKind.Forge && kind != LoaderKind.NeoForge)
                throw new ArgumentException("ForgeInstaller handles forge and neoforge only", nameof(kind));

            Kind = kind;
        }

        public LoaderKind Kind { get; }

        public string DisplayName => Kind == LoaderKind.Forge ? "Forge" : "NeoForge";

        public async Task ApplyAsync(InstallContext context, InstallPlan plan, string gameVersion, LoaderSpec loader,
            string profileId, CancellationToken ct)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var coordinate = BuildInstallerCoordinate(Kind, gameVersion, loader?.Version);
            var relative = VanillaResolver.MavenRelativePath(coordinate);
            var fileName = Path.GetFileName(relative.Replace('/', Path.DirectorySeparatorChar));
            var url = MavenBase + relative;
            var targetPath = Path.Combine(plan.Root, ".mcdl", "installers", fileName);

            var installerVersion = ComposeInstallerVersion(Kind, gameVersion, loader?.Version);
            var requiredJava = RequiredJavaMajor(plan.Detail?.JavaVersion?.MajorVersion ?? 0, gameVersion);

            context.Log?.Invoke("下载 " + DisplayName + " " + installerVersion + " 的官方 installer：" + fileName);
            if (plan.Detail?.JavaVersion == null || plan.Detail.JavaVersion.MajorVersion <= 0)
                context.Log?.Invoke("原版元数据没写需要的 Java 版本，按 " + gameVersion + " 的要求用 Java " + requiredJava);

            var sha1 = await FetchExpectedSha1Async(context, url, fileName, ct).ConfigureAwait(false);

            plan.Files.Add(new FileTask
            {
                Label = fileName,
                Url = url,
                TargetPath = targetPath,
                Sha1 = sha1,
                Kind = SourceKind.Maven
            });

            plan.PostDownload.Add(token => RunInstallerAsync(context, plan, targetPath, installerVersion,
                requiredJava, profileId, token));
        }

        /// <summary>
        /// 拉 installer 旁边的 .sha1 来校验。拿不到就返回 null：此时仍然安装，但日志里说清楚没校验。
        /// 这一步存在的原因是这个 jar 会被本机 Java 执行，属于“下载并执行二次载荷”，能校验就别放过。
        /// </summary>
        private async Task<string> FetchExpectedSha1Async(InstallContext context, string url, string fileName,
            CancellationToken ct)
        {
            try
            {
                var listing = await context.Engine.GetTextAsync(url + ".sha1", SourceKind.Maven, ct).ConfigureAwait(false);
                var sha1 = ParseSha1Listing(listing);
                if (sha1 != null)
                {
                    context.Log?.Invoke("已取到 " + fileName + " 的上游 .sha1，下载后按它校验");
                    return sha1;
                }

                context.Log?.Invoke("上游 .sha1 内容不像哈希，跳过对 " + fileName + " 的校验");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                context.Log?.Invoke("拿不到 " + fileName + " 的上游 .sha1（" + ex.Message + "），这次不校验它，只校验安装结果");
            }

            return null;
        }

        /// <summary>处理 Maven 的 .sha1 两种写法："&lt;hash&gt;" 与 "&lt;hash&gt;  &lt;文件名&gt;"。</summary>
        internal static string ParseSha1Listing(string listing)
        {
            return Hashes.ParseSha1Listing(listing);
        }

        private string MavenBase => Kind == LoaderKind.Forge ? ForgeMavenBase : NeoForgeMavenBase;

        /// <summary>
        /// 把包里的加载器版本拼成 Maven 里真正用的版本串。
        /// Forge 的 installer 版本永远是 "&lt;游戏版本&gt;-&lt;构建号&gt;"；NeoForge 从 1.20.2 起
        /// 改用独立版本号（20.4.237），坐标也不再带游戏版本，1.20.1 还沿用 Forge 那套。
        /// </summary>
        internal static string ComposeInstallerVersion(LoaderKind kind, string gameVersion, string loaderVersion)
        {
            if (kind != LoaderKind.Forge && kind != LoaderKind.NeoForge)
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "ForgeInstaller 只处理 forge / neoforge");

            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new ArgumentException("缺少 Minecraft 版本号，没法定位 " + KindName(kind) + " 的 installer", nameof(gameVersion));

            if (string.IsNullOrWhiteSpace(loaderVersion))
                throw new ArgumentException(
                    "整合包没有写 " + KindName(kind) + " 的版本号，没法自己挑最新的 installer；" +
                    "可以在包的依赖里补上版本，或者先单独装原版再补加载器。", nameof(loaderVersion));

            var game = gameVersion.Trim();
            var version = loaderVersion.Trim();

            // 有些包（比如 HMCL 导出的）直接写完整的 "1.20.1-47.2.0"，别重复拼前缀。
            if (version.StartsWith(game + "-", StringComparison.OrdinalIgnoreCase)) return version;

            if (kind == LoaderKind.NeoForge && IsVersionAtLeast(game, "1.20.2")) return version;

            return game + "-" + version;
        }

        internal static string BuildInstallerCoordinate(LoaderKind kind, string gameVersion, string loaderVersion)
        {
            var artifact = InstallerArtifact(kind, gameVersion);
            var version = ComposeInstallerVersion(kind, gameVersion, loaderVersion);
            return artifact + ":" + version + ":installer";
        }

        internal static string InstallerFileName(LoaderKind kind, string gameVersion, string loaderVersion)
        {
            var relative = VanillaResolver.MavenRelativePath(BuildInstallerCoordinate(kind, gameVersion, loaderVersion));
            return Path.GetFileName(relative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>installer 装完之后在 versions 下应出现的目录名，例如 "forge-1.20.1-47.2.0"。</summary>
        internal static string ExpectedVersionId(LoaderKind kind, string installerVersion)
        {
            return (kind == LoaderKind.Forge ? "forge-" : "neoforge-") + installerVersion;
        }

        /// <summary>原版元数据没给 Java 版本时的兜底，按 Mojang 公布的最低要求来。</summary>
        internal static int RequiredJavaMajor(int declaredMajor, string gameVersion)
        {
            if (declaredMajor > 0) return declaredMajor;

            if (IsVersionAtLeast(gameVersion, "1.20.5")) return 21;
            if (IsVersionAtLeast(gameVersion, "1.18")) return 17;
            if (IsVersionAtLeast(gameVersion, "1.17")) return 16;
            return 8;
        }

        /// <summary>按数字段比较版本，用来判断 "1.20.10" 这类带多位的号。</summary>
        internal static bool IsVersionAtLeast(string version, string reference)
        {
            var left = VersionNumbers(version);
            var right = VersionNumbers(reference);
            if (left.Count == 0) return false;

            var count = Math.Max(left.Count, right.Count);
            for (var i = 0; i < count; i++)
            {
                var a = i < left.Count ? left[i] : 0;
                var b = i < right.Count ? right[i] : 0;
                if (a != b) return a > b;
            }

            return true;
        }

        private static List<int> VersionNumbers(string value)
        {
            var numbers = new List<int>();
            if (string.IsNullOrWhiteSpace(value)) return numbers;

            foreach (var part in value.Split('.', '-', '_', '+'))
            {
                int number;
                if (!int.TryParse(part, out number)) break; // 快照之类的尾巴不参与比较
                numbers.Add(number);
            }

            return numbers;
        }

        private static string InstallerArtifact(LoaderKind kind, string gameVersion)
        {
            if (kind == LoaderKind.Forge) return "net.minecraftforge:forge";
            if (kind == LoaderKind.NeoForge)
                return IsVersionAtLeast(gameVersion, "1.20.2") ? "net.neoforged:neoforge" : "net.neoforged:forge";

            throw new ArgumentOutOfRangeException(nameof(kind), kind, "ForgeInstaller 只处理 forge / neoforge");
        }

        private static string KindName(LoaderKind kind)
        {
            return kind == LoaderKind.Forge ? "Forge" : "NeoForge";
        }

        private async Task RunInstallerAsync(InstallContext context, InstallPlan plan, string installerPath,
            string installerVersion, int requiredJava, string profileId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var java = JavaLocator.Find(requiredJava);
            if (java == null)
                throw new InvalidOperationException(
                    DisplayName + " 的 installer 需要 Java " + requiredJava + " 或更高版本，但本机没找到能用的 java.exe。" +
                    "可以装一个 JDK " + requiredJava + "+，或者把 java.exe 所在目录加进 PATH，然后再装一次。");

            context.Log?.Invoke("用 Java " + java.Version + " 运行 " + DisplayName + " installer，目标目录 " + plan.Root);

            var versionsRoot = Path.Combine(plan.Root, "versions");
            var before = ExistingVersionDirs(versionsRoot);

            RunInstallerProcess(context, java.Path, installerPath, plan.Root, ct);

            var created = ExistingVersionDirs(versionsRoot).Where(dir => !before.Contains(dir)).ToList();
            var installedId = FindInstalledVersion(versionsRoot, created, installerVersion);
            if (installedId == null)
                throw new InvalidOperationException(
                    DisplayName + " installer 正常退出，但 versions 目录下没有出现新的 " + DisplayName +
                    " 版本目录，安装不算成功。installer 会自己去连 launchermeta.mojang.com、" +
                    "libraries.minecraft.net 这些官方域名，不走 McDl 的镜像；如果代理、hosts 或网络工具" +
                    "拦了它们，installer 可能什么都不做就退出，先确认这些域名能访问再重试。");

            if (!string.IsNullOrWhiteSpace(profileId))
            {
                // 让整合包在启动器里以自己的名字出现，同时继承刚装好的加载器版本。
                var profilePath = Path.Combine(versionsRoot, profileId, profileId + ".json");
                Json.Save(profilePath, new
                {
                    id = profileId,
                    inheritsFrom = installedId,
                    type = "release"
                });
            }

            context.Log?.Invoke(DisplayName + " " + installerVersion + " 安装完成，版本目录 " + installedId);
        }

        private string FindInstalledVersion(string versionsRoot, IEnumerable<string> created, string installerVersion)
        {
            // 正常情况目录名是 forge-<版本> / neoforge-<版本>；1.20.1 的老 NeoForge 装出来可能是 forge-。
            var expected = new[]
            {
                "forge-" + installerVersion,
                "neoforge-" + installerVersion,
                ExpectedVersionId(Kind, installerVersion)
            };

            foreach (var name in expected)
                if (HasVersionJson(versionsRoot, name)) return name;

            // installer 以后改了命名规则也不至于误判，这一轮新出现且带 json 的目录也算数。
            foreach (var name in created)
                if (HasVersionJson(versionsRoot, name)) return name;

            return null;
        }

        private static bool HasVersionJson(string versionsRoot, string versionId)
        {
            return File.Exists(Path.Combine(versionsRoot, versionId, versionId + ".json"));
        }

        private static HashSet<string> ExistingVersionDirs(string versionsRoot)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(versionsRoot)) return result;
                foreach (var dir in Directory.GetDirectories(versionsRoot))
                    result.Add(Path.GetFileName(dir));
            }
            catch (Exception)
            {
                // 读不到目录时按“没有旧版本”处理，后面的校验会兜住。
            }

            return result;
        }

        private void RunInstallerProcess(InstallContext context, string javaPath, string installerPath, string root,
            CancellationToken ct)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-jar \"" + installerPath + "\" --installClient \"" + root + "\"",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                    throw new InvalidOperationException("启动 Java 进程失败：" + javaPath);

                var tail = new Queue<string>();
                var gate = new object();
                var prefix = "[" + DisplayName.ToLowerInvariant() + "] ";
                var stdout = Task.Run(() => Pump(process.StandardOutput, context.Log, prefix, tail, gate));
                var stderr = Task.Run(() => Pump(process.StandardError, context.Log, prefix, tail, gate));

                var clock = Stopwatch.StartNew();
                var timedOut = false;
                var cancelled = false;

                while (!process.WaitForExit(200))
                {
                    if (ct.IsCancellationRequested)
                    {
                        cancelled = true;
                        TryKill(process);
                        process.WaitForExit();
                        break;
                    }

                    if (clock.Elapsed > InstallTimeout)
                    {
                        timedOut = true;
                        TryKill(process);
                        process.WaitForExit();
                        break;
                    }
                }

                // 进程退掉后两个流才会关闭；等读取线程把剩下的输出收完，避免丢日志。
                Task.WaitAll(stdout, stderr);

                if (cancelled) throw new OperationCanceledException(ct);
                if (timedOut)
                    throw new TimeoutException(DisplayName + " installer 跑了超过 " +
                                               (int)InstallTimeout.TotalMinutes + " 分钟还没结束，已经强制结束。");

                if (process.ExitCode == 0) return;

                string output;
                lock (gate) output = string.Join(Environment.NewLine, tail);
                throw new InvalidOperationException(DisplayName + " installer 退出码 " + process.ExitCode +
                                                    "，最后几行输出：" + Environment.NewLine +
                                                    (output.Length == 0 ? "(安装器没有输出)" : output));
            }
        }

        private static void Pump(StreamReader reader, Action<string> log, string prefix, Queue<string> tail, object gate)
        {
            try
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;

                    log?.Invoke(prefix + line);
                    lock (gate)
                    {
                        tail.Enqueue(line);
                        while (tail.Count > TailLines) tail.Dequeue();
                    }
                }
            }
            catch (Exception)
            {
                // 进程被结束时流可能直接断掉；这里只负责转发输出，不吃掉真正的问题。
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (Exception)
            {
                // 已经退了就算了。
            }
        }
    }
}
