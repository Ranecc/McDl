using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using McDl.Core;

namespace McDl.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (Exception)
            {
                // Redirected consoles can refuse a code page change; ASCII fallback is fine.
            }

            try
            {
                return RunAsync(args).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("cancelled");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return 1;
            }
        }

        private static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintHelp();
                return 0;
            }

            var command = args[0].ToLowerInvariant();
            var parsed = CommandLine.Parse(args.Skip(1).ToArray());

            using (var cts = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                switch (command)
                {
                    case "list":
                        return await ListCommand.RunAsync(parsed, cts.Token).ConfigureAwait(false);
                    case "plan":
                        return await PlanCommand.RunAsync(parsed, cts.Token).ConfigureAwait(false);
                    case "install":
                        return await InstallCommand.RunAsync(parsed, cts.Token).ConfigureAwait(false);
                    case "modpack":
                        return await ModpackCommand.RunAsync(parsed, cts.Token).ConfigureAwait(false);
                    default:
                        Console.Error.WriteLine("unknown command: " + command);
                        PrintHelp();
                        return 1;
                }
            }
        }

        private static bool IsHelp(string value)
        {
            return value == "-h" || value == "--help" || value == "help";
        }

        private static void PrintHelp()
        {
            Console.WriteLine("mcdl - standalone Minecraft file downloader");
            Console.WriteLine();
            Console.WriteLine("usage:");
            Console.WriteLine("  mcdl list [--type release|snapshot] [--limit N]");
            Console.WriteLine("  mcdl plan <version> [--root DIR] [--no-assets]");
            Console.WriteLine("  mcdl install <version> [--root DIR] [--no-assets] [--threads N]");
            Console.WriteLine("  mcdl modpack <file.mrpack> [--root DIR] [--no-assets] [--threads N]");
            Console.WriteLine();
            Console.WriteLine("version: an exact id such as 1.21.4, or \"latest\" / \"latest-snapshot\"");
            Console.WriteLine("modpack: a Modrinth .mrpack file; the game body it requires is installed");
            Console.WriteLine("root   : target .minecraft directory (defaults to ./.minecraft)");
        }
    }

    internal sealed class CommandLine
    {
        private readonly Dictionary<string, string> _options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _positional = new List<string>();

        public static CommandLine Parse(string[] args)
        {
            var result = new CommandLine();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    var name = arg.Substring(2);
                    string value = null;

                    var eq = name.IndexOf('=');
                    if (eq >= 0)
                    {
                        value = name.Substring(eq + 1);
                        name = name.Substring(0, eq);
                    }
                    else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        value = args[++i];
                    }

                    result._options[name] = value ?? "true";
                }
                else
                {
                    result._positional.Add(arg);
                }
            }
            return result;
        }

        public string First => _positional.Count > 0 ? _positional[0] : null;

        public bool Has(string name) => _options.ContainsKey(name);

        public string Option(string name, string fallback = null)
        {
            string value;
            return _options.TryGetValue(name, out value) && value != null ? value : fallback;
        }

        public int IntOption(string name, int fallback)
        {
            int parsed;
            return _options.TryGetValue(name, out var raw) && int.TryParse(raw, out parsed) ? parsed : fallback;
        }

        public string Root => Path.GetFullPath(Option("root", Path.Combine(Environment.CurrentDirectory, ".minecraft")));

        /// <summary>Shared request shape; every command builds the same object the UI builds.</summary>
        public InstallRequest ToRequest(InstallKind kind, string version = null)
        {
            return new InstallRequest
            {
                Kind = kind,
                Root = Root,
                Version = version ?? First ?? "latest",
                IncludeAssets = !Has("no-assets")
            };
        }
    }

    internal static class ListCommand
    {
        public static async Task<int> RunAsync(CommandLine cli, CancellationToken ct)
        {
            using (var installer = new Installer(MirrorRouter.CreateDefault(), 4))
            {
                var manifest = await installer.ListVersionsAsync(ct).ConfigureAwait(false);
                var versions = manifest?.Versions;
                if (versions == null || versions.Count == 0)
                {
                    Console.Error.WriteLine("version manifest is unreadable");
                    return 1;
                }

                var type = cli.Option("type");
                var limit = cli.IntOption("limit", 40);

                Console.WriteLine("versions: " + versions.Count);
                Console.WriteLine();

                var rows = versions
                    .Where(v => type == null || string.Equals(v.Type, type, StringComparison.OrdinalIgnoreCase))
                    .Take(limit);

                foreach (var row in rows)
                    Console.WriteLine(string.Format("{0,-18} {1,-10} {2}", row.Id, row.Type, row.ReleaseTime));
            }
            return 0;
        }
    }

    internal static class PlanCommand
    {
        public static async Task<int> RunAsync(CommandLine cli, CancellationToken ct)
        {
            using (var installer = new Installer(MirrorRouter.CreateDefault(), 4))
            {
                var plan = await installer.ResolveAsync(cli.ToRequest(InstallKind.Vanilla), ct).ConfigureAwait(false);
                PrintPlan(plan);
            }
            return 0;
        }

        public static void PrintPlan(InstallPlan plan)
        {
            var byKind = plan.Files.GroupBy(f => f.Kind)
                .Select(g => new { Kind = g.Key, Count = g.Count(), Bytes = g.Sum(x => x.Size) })
                .OrderByDescending(g => g.Bytes);

            Console.WriteLine("name         : " + plan.Label);
            Console.WriteLine("version      : " + plan.VersionId);
            Console.WriteLine("asset index  : " + plan.AssetIndexId + (plan.VirtualAssets ? " (virtual)" : string.Empty));
            Console.WriteLine("root         : " + plan.Root);
            Console.WriteLine("files        : " + plan.Files.Count);
            Console.WriteLine("size         : " + Format.Bytes(plan.TotalBytes));
            Console.WriteLine();

            foreach (var group in byKind)
                Console.WriteLine(string.Format("  {0,-8} {1,6} files  {2,10}", group.Kind, group.Count, Format.Bytes(group.Bytes)));

            if (plan.Natives.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("natives to unpack:");
                foreach (var native in plan.Natives)
                    Console.WriteLine("  " + Path.GetFileName(native.Archive.TargetPath));
            }
        }
    }

    internal static class InstallCommand
    {
        public static async Task<int> RunAsync(CommandLine cli, CancellationToken ct)
        {
            return await InstallAsync(cli, cli.ToRequest(InstallKind.Vanilla), ct).ConfigureAwait(false);
        }

        /// <summary>Shared by "install" and "modpack": resolve, download, print the same summary.</summary>
        public static async Task<int> InstallAsync(CommandLine cli, InstallRequest request, CancellationToken ct)
        {
            var threads = cli.IntOption("threads", 8);

            using (var installer = new Installer(MirrorRouter.CreateDefault(), threads))
            {
                var target = request.Kind == InstallKind.Modpack ? request.ModpackPath : request.Version;
                Console.Write("resolving " + target + " ... ");

                var progress = new ConsoleProgress();
                var summary = await installer.InstallAsync(request, progress, ct, plan =>
                {
                    Console.WriteLine(plan.Label + " (" + plan.VersionId + "), " + plan.Files.Count + " files, " +
                                      Format.Bytes(plan.TotalBytes));
                }).ConfigureAwait(false);

                Console.WriteLine();
                if (summary.Stats != null)
                {
                    Console.WriteLine("done: " + summary.Stats.DoneFiles + " downloaded, " + summary.Stats.SkippedFiles +
                                      " already present, " + summary.Stats.FailedFiles + " failed");
                }

                Console.WriteLine("root: " + (summary.Plan != null ? summary.Plan.Root : request.ResolveRoot()));

                if (summary.Error != null) Console.Error.WriteLine("error: " + summary.Error);
                if (summary.Failures != null && summary.Failures.Count > 0)
                    Console.Error.WriteLine("first failure: " + summary.Failures[0]);

                return summary.Succeeded ? 0 : 1;
            }
        }
    }

    internal static class ModpackCommand
    {
        public static async Task<int> RunAsync(CommandLine cli, CancellationToken ct)
        {
            var path = cli.First;
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.Error.WriteLine("usage: mcdl modpack <file.mrpack> [--root DIR]");
                return 1;
            }

            path = Path.GetFullPath(path);
            var request = cli.ToRequest(InstallKind.Modpack);
            request.ModpackPath = path;

            return await InstallCommand.InstallAsync(cli, request, ct).ConfigureAwait(false);
        }
    }

    internal sealed class ConsoleProgress : IProgress<TransferStats>
    {
        private const int Width = 30;

        public void Report(TransferStats value)
        {
            if (value == null) return;

            var filled = (int)Math.Round(value.Percent / 100.0 * Width);
            if (filled < 0) filled = 0;
            if (filled > Width) filled = Width;

            var bar = new string('#', filled) + new string('-', Width - filled);
            var line = string.Format(CultureInfo.InvariantCulture,
                "[{0}] {1,5:0.0}%  {2}/{3}  {4}/s  {5}",
                bar, value.Percent,
                Format.Bytes(value.DoneBytes), Format.Bytes(value.TotalBytes),
                Format.Bytes((long)value.BytesPerSecond),
                Shorten(value.CurrentFile, 34));

            try
            {
                Console.Write("\r" + line.PadRight(Console.WindowWidth > line.Length ? Console.WindowWidth - 1 : line.Length));
            }
            catch (IOException)
            {
                // Console may be redirected; progress is cosmetic.
            }
        }

        private static string Shorten(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= max ? value : "..." + value.Substring(value.Length - (max - 3));
        }
    }

    internal static class Format
    {
        public static string Bytes(long value)
        {
            if (value < 1024) return value + " B";
            if (value < 1024L * 1024) return (value / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            if (value < 1024L * 1024 * 1024) return (value / 1024.0 / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return (value / 1024.0 / 1024 / 1024).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
