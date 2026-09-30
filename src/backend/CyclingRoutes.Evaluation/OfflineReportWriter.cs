using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyclingRoutes.Evaluation;

public static class OfflineReportWriter
{
    public static async Task WriteAsync(string outputPath, OfflineRouteReport report, CancellationToken ct)
    {
        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
            {
                await JsonSerializer.SerializeAsync(output, report, options, ct);
                await output.FlushAsync(ct);
            }
            ct.ThrowIfCancellationRequested();
            // Publish only a fully written report; never replace existing evidence.
            File.Move(temporaryPath, outputPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
