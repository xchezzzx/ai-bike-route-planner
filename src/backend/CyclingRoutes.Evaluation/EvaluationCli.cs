using System.Globalization;
using System.Text.Json;

namespace CyclingRoutes.Evaluation;

public static class EvaluationCli
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length is not (3 or 5) || (args[0] == "gpx" ? args.Length != 3 : args[0] is not ("ors" or "graphhopper") || args.Length != 5))
            {
                Console.Error.WriteLine("Usage: gpx INPUT OUTPUT | ors|graphhopper INPUT OUTPUT MIN_METERS MAX_METERS");
                return 2;
            }
            var inputPath = Path.GetFullPath(args[1]);
            var outputPath = Path.GetFullPath(args[2]);
            if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
            await using var input = File.OpenRead(inputPath);
            if (input.Length is 0 or > OfflineRouteEvaluator.MaxInputBytes) throw new InvalidDataException();
            var bytes = new byte[(int)input.Length];
            await input.ReadExactlyAsync(bytes);
            var report = args[0] == "gpx"
                ? await OfflineRouteEvaluator.GpxAsync(bytes, CancellationToken.None)
                : args[0] == "graphhopper"
                ? await OfflineRouteEvaluator.GraphHopperAsync(bytes,
                    double.Parse(args[3], CultureInfo.InvariantCulture),
                    double.Parse(args[4], CultureInfo.InvariantCulture), CancellationToken.None)
                : await OfflineRouteEvaluator.OrsAsync(bytes,
                    double.Parse(args[3], CultureInfo.InvariantCulture),
                    double.Parse(args[4], CultureInfo.InvariantCulture), CancellationToken.None);
            await OfflineReportWriter.WriteAsync(outputPath, report, CancellationToken.None);
            Console.WriteLine("Offline report written. No network calls; eligibility is not a safety certification.");
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or FormatException or OverflowException or System.Xml.XmlException or Application.Routing.RoutingException or JsonException)
        {
            Console.Error.WriteLine("Offline evaluation failed. Input/report files were not overwritten.");
            return 1;
        }
    }
}
