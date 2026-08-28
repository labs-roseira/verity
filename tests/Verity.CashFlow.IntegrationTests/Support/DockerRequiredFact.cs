using System.Diagnostics;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Support;

public sealed class DockerRequiredFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> DockerAvailable = new(CheckDocker);

    public DockerRequiredFactAttribute()
    {
        if (!DockerAvailable.Value)
            Skip = "Docker is not available.";
    }

    private static bool CheckDocker()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return false;

            process.WaitForExit(10000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
