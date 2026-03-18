using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace BlockUpdateWindowsDefender.Services
{
    public class ProcessRunner
    {
        public async Task<ProcessResult> RunAsync(string fileName, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await Task.Run(() => process.WaitForExit());

                return new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = await outputTask,
                    StandardError = await errorTask
                };
            }
        }

        public Task<ProcessResult> RunPowerShellAsync(string script)
        {
            var bytes = Encoding.Unicode.GetBytes(script);
            var encodedCommand = Convert.ToBase64String(bytes);
            return RunAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}");
        }
    }
}
