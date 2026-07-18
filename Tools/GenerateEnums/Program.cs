using System;

namespace GenerateEnums;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "diff")
        {
            Console.Error.WriteLine("diff is not implemented yet."); // wired in a later task
            return 2;
        }
        if (args.Length > 0 && args[0] == "generate")
            return EnumGenerator.Run(args[1..]);
        // bare invocation or `<path>`/`--force` — legacy generate behavior
        return EnumGenerator.Run(args);
    }
}
