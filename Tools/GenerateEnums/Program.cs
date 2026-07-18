using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace GenerateEnums;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "diff")
            return RunDiff(args[1..]);
        if (args.Length > 0 && args[0] == "generate")
            return EnumGenerator.Run(args[1..]);
        // bare invocation or `<path>`/`--force` — legacy generate behavior
        return EnumGenerator.Run(args);
    }

    private static int RunDiff(string[] args)
    {
        string? url = null;
        string? definitionsPath = null;
        bool json = false;
        int timeoutSec = 15;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": json = true; break;
                case "--definitions": definitionsPath = ArgValue(args, ref i, "--definitions"); break;
                case "--timeout": timeoutSec = int.Parse(ArgValue(args, ref i, "--timeout")); break;
                default:
                    if (args[i].StartsWith('-')) { Console.Error.WriteLine($"Unknown option '{args[i]}'."); return 2; }
                    if (url is not null) { Console.Error.WriteLine("Only one URL may be given."); return 2; }
                    url = args[i];
                    break;
            }
        }

        if (url is null)
        {
            Console.Error.WriteLine("Usage: diff <ws-or-http-url> [--json] [--definitions <path>] [--timeout <sec>]");
            return 2;
        }

        definitionsPath ??= Path.Combine(EnumGenerator.RepoRoot(), "Base", "Xrpl.BinaryCodec", "Enums", "definitions.json");
        if (!File.Exists(definitionsPath))
        {
            Console.Error.WriteLine($"Local definitions.json not found at: {definitionsPath}");
            return 2;
        }

        Definitions local;
        Definitions server;
        try
        {
            using JsonDocument localDoc = JsonDocument.Parse(File.ReadAllText(definitionsPath));
            local = Definitions.Parse(localDoc.RootElement);
            server = ServerDefinitionsClient
                .FetchAsync(url, TimeSpan.FromSeconds(timeoutSec), CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"diff failed: {ex.Message}");
            return 2;
        }

        DiffResult result = DefinitionsDiff.Compare(local, server);
        Console.WriteLine(json ? DiffRenderer.RenderJson(result) : DiffRenderer.RenderTable(result));
        return result.HasDrift ? 1 : 0;
    }

    private static string ArgValue(string[] args, ref int i, string name)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{name} requires a value.");
        return args[++i];
    }
}
