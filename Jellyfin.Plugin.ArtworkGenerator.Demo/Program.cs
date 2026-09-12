using System;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ArtworkGenerator.DemoGenerator
{
    /// <summary>
    /// Console application to generate demo poster images from templates.
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   Artwork Generator - Demo Image Generator          ║");
            Console.WriteLine("║   Generates example posters from all template configs      ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            try
            {
                var generator = new DemoImageGenerator();
                await generator.GenerateAllDemosAsync();

                Console.WriteLine();
                Console.WriteLine("  Success!");
                Console.WriteLine($"  Output location: ../docs/examples/");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"  Error generating demos: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Environment.Exit(1);
            }
        }
    }
}
