using NAP.Core;

namespace NAP.Cli;

internal static class Program
{
    private static void Main()
    {
        Console.WriteLine($"{ApplicationIdentity.Name} — {ApplicationIdentity.FullName}");
        Console.WriteLine("Status: Ready");
    }
}
