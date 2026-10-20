using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace McDl.Core
{
    /// <summary>本机上的一个 Java 运行时。</summary>
    public sealed class JavaRuntime
    {
        public JavaRuntime(string path, string version, int majorVersion)
        {
            Path = path;
            Version = version;
            MajorVersion = majorVersion;
        }

        /// <summary>java.exe 的完整路径。</summary>
        public string Path { get; }

        /// <summary>完整版本字符串，例如 "17.0.12"。</summary>
        public string Version { get; }

        /// <summary>主版本号，例如 8 / 17 / 21。</summary>
        public int MajorVersion { get; }

        public override string ToString()
        {
            return Version + " (" + Path + ")";
        }
    }

    /// <summary>
    /// 找本机可用的 Java。Forge / NeoForge 的官方 installer 必须靠它来跑。
    /// 查找顺序与判定规则见 docs/loaders.md。
    /// </summary>
    public static class JavaLocator
    {
        private static readonly string[] JavaSoftKeys =
        {
            @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\JavaSoft\Java Development Kit"
        };

        private static readonly string[] ProgramFilesJavaRoots =
        {
            "Java",
            "Eclipse Adoptium",
            "Microsoft"
        };

        /// <summary>列出找到的所有 Java，按主版本号从高到低。</summary>
        public static IReadOnlyList<JavaRuntime> Discover()
        {
            return Discover(null);
        }

        /// <summary>
        /// 找出本机所有 Java，按主版本号从高到低排列。
        /// <paramref name="extraRoots"/> 一般是调用方手里的 .minecraft 目录（用来找 Mojang 下发的
        /// runtime），也可以直接传 Java 安装目录；每个都能被探出真实版本。
        /// </summary>
        public static IReadOnlyList<JavaRuntime> Discover(IEnumerable<string> extraRoots)
        {
            var candidates = new List<string>();

            CollectJavaHome(candidates);
            CollectFromPath(candidates);
            CollectFromRegistry(candidates);
            CollectFromProgramFiles(candidates);
            CollectFromExtraRoots(candidates, extraRoots);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var runtimes = new List<JavaRuntime>();

            foreach (var candidate in candidates)
            {
                string full;
                try
                {
                    full = Path.GetFullPath(candidate);
                }
                catch (Exception)
                {
                    continue;
                }

                if (!seen.Add(full) || !File.Exists(full)) continue;

                // 路径存在不代表能跑；版本以 java -version 的实际输出为准。
                var runtime = Probe(full);
                if (runtime != null) runtimes.Add(runtime);
            }

            // LINQ 的排序是稳定的，同主版本的 Java 保持上面的发现顺序。
            return runtimes.OrderByDescending(r => r.MajorVersion).ToList();
        }

        /// <summary>挑一个主版本号不低于 requiredMajor 的 Java；找不到返回 null。</summary>
        public static JavaRuntime Find(int requiredMajor)
        {
            var runtimes = Discover();
            if (runtimes.Count == 0) return null;

            // 老版 Forge 的 installer 只认自己那一代 Java（1.12 只能 Java 8），
            // 所以优先精确匹配，避免拿 Java 21 去跑只支持低版本的安装器。
            foreach (var runtime in runtimes)
                if (runtime.MajorVersion == requiredMajor) return runtime;

            // 没有精确匹配时用“能跑的最低版本”，仍然满足不低于要求。
            JavaRuntime fallback = null;
            foreach (var runtime in runtimes)
            {
                if (runtime.MajorVersion <= requiredMajor) continue;
                if (fallback == null || runtime.MajorVersion < fallback.MajorVersion) fallback = runtime;
            }

            return fallback;
        }

        private static void CollectJavaHome(List<string> paths)
        {
            var home = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (string.IsNullOrWhiteSpace(home)) return;

            var trimmed = home.Trim().Trim('"').TrimEnd('\\', '/');
            if (trimmed.Length == 0) return;

            paths.Add(Path.Combine(trimmed, "bin", "java.exe"));
            // 有的安装脚本把 JAVA_HOME 直接指到 bin 目录。
            paths.Add(Path.Combine(trimmed, "java.exe"));
        }

        private static void CollectFromPath(List<string> paths)
        {
            var raw = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(raw)) return;

            foreach (var segment in raw.Split(';'))
            {
                var dir = segment.Trim().Trim('"');
                if (dir.Length > 0) paths.Add(Path.Combine(dir, "java.exe"));
            }
        }

        private static void CollectFromRegistry(List<string> paths)
        {
            foreach (var keyPath in JavaSoftKeys)
            {
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
                    {
                        var current = key?.GetValue("CurrentVersion") as string;
                        if (string.IsNullOrEmpty(current)) continue;

                        using (var versionKey = key.OpenSubKey(current))
                            AddJavaHome(paths, versionKey?.GetValue("JavaHome") as string);
                    }
                }
                catch (Exception)
                {
                    // 注册表被策略挡住就当没有。
                }
            }

            try
            {
                using (var adoptium = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Eclipse Adoptium"))
                {
                    if (adoptium == null) return;

                    foreach (var name in adoptium.GetSubKeyNames())
                    {
                        using (var subKey = adoptium.OpenSubKey(name))
                        {
                            // Adoptium 的安装器把安装目录写在 Path 里，旧版本写 JavaHome。
                            AddJavaHome(paths, subKey?.GetValue("Path") as string);
                            AddJavaHome(paths, subKey?.GetValue("JavaHome") as string);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 同上。
            }
        }

        private static void CollectFromProgramFiles(List<string> paths)
        {
            var roots = new List<string>();
            foreach (var folder in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                     })
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                foreach (var name in ProgramFilesJavaRoots)
                    roots.Add(Path.Combine(folder, name));
            }

            foreach (var root in roots)
                AddChildJavaHomes(paths, root);
        }

        private static void CollectFromExtraRoots(List<string> paths, IEnumerable<string> extraRoots)
        {
            if (extraRoots == null) return;

            foreach (var raw in extraRoots)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                string root;
                try
                {
                    root = Path.GetFullPath(raw.Trim());
                }
                catch (Exception)
                {
                    continue;
                }

                if (!Directory.Exists(root)) continue;

                // 传进来的目录本身可能就是某个 JDK，也可能是装着若干 JDK 的父目录。
                AddJavaHome(paths, root);
                AddChildJavaHomes(paths, root);

                // Mojang 运行时是 runtime\<类型>\<平台>\<版本>\bin\java.exe，层级不固定，直接递归找。
                CollectFromRuntime(paths, Path.Combine(root, "runtime"));
                CollectFromRuntime(paths, Path.Combine(root, ".minecraft", "runtime"));
            }
        }

        private static void CollectFromRuntime(List<string> paths, string runtimeRoot)
        {
            if (!Directory.Exists(runtimeRoot)) return;

            try
            {
                foreach (var exe in Directory.GetFiles(runtimeRoot, "java.exe", SearchOption.AllDirectories))
                {
                    var dir = Path.GetDirectoryName(exe);
                    if (dir == null || !string.Equals(Path.GetFileName(dir), "bin", StringComparison.OrdinalIgnoreCase))
                        continue;

                    paths.Add(exe);
                }
            }
            catch (Exception)
            {
                // 目录不可读就跳过，不因为它让整次查找失败。
            }
        }

        private static void AddChildJavaHomes(List<string> paths, string root)
        {
            if (!Directory.Exists(root)) return;

            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                    AddJavaHome(paths, dir);
            }
            catch (Exception)
            {
                // 同上。
            }
        }

        private static void AddJavaHome(List<string> paths, string home)
        {
            if (string.IsNullOrWhiteSpace(home)) return;

            var trimmed = home.Trim().Trim('"').TrimEnd('\\', '/');
            if (trimmed.Length == 0) return;

            paths.Add(Path.Combine(trimmed, "bin", "java.exe"));
        }

        /// <summary>跑一次 java -version 解析主版本号；失败返回 null。</summary>
        internal static JavaRuntime Probe(string javaPath)
        {
            if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath)) return null;

            try
            {
                var info = new ProcessStartInfo(javaPath, "-version")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(info))
                {
                    if (process == null) return null;

                    // 两个流都要读，否则管道写满后 java 会卡住，WaitForExit 也跟着卡。
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(5000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                            // 已经退了就没什么可杀的。
                        }

                        return null;
                    }

                    var text = stdout.Result + "\n" + stderr.Result;
                    foreach (var line in text.Split('\n'))
                    {
                        var runtime = ParseVersionLine(javaPath, line);
                        if (runtime != null) return runtime;
                    }
                }
            }
            catch (Exception)
            {
                // 探不动就当没有。
            }

            return null;
        }

        /// <summary>解析 “openjdk version "17.0.12" 2024-07-16” 这种行。</summary>
        internal static JavaRuntime ParseVersionLine(string javaPath, string line)
        {
            if (string.IsNullOrEmpty(line)) return null;

            var marker = line.IndexOf("version", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return null;

            var start = line.IndexOf('"', marker);
            if (start < 0) return null;
            var end = line.IndexOf('"', start + 1);
            if (end < 0) return null;

            var version = line.Substring(start + 1, end - start - 1);
            var major = ParseMajor(version);
            if (major <= 0) return null;

            return new JavaRuntime(javaPath, version, major);
        }

        /// <summary>"1.8.0_402" → 8，"21.0.1" → 21。</summary>
        internal static int ParseMajor(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return 0;

            var parts = version.Split('.');
            int number;

            if (parts[0] == "1" && parts.Length > 1)
                return int.TryParse(parts[1], out number) ? number : 0;

            var head = parts[0].Split('-', '+')[0];
            return int.TryParse(head, out number) ? number : 0;
        }
    }
}
