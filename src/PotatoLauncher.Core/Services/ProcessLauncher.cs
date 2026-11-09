using System.Diagnostics;
using System.Text;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.Core.Services;

public interface IProcessLauncher
{
    Task<LaunchProcessResult> LaunchAsync(
        LaunchPlan plan,
        Action<string> onOutput,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessLauncher : IProcessLauncher
{
    public async Task<LaunchProcessResult> LaunchAsync(
        LaunchPlan plan,
        Action<string> onOutput,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = plan.ExecutablePath,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = ResolveEncoding(),
            StandardErrorEncoding = ResolveEncoding()
        };
        foreach (var argument in plan.Arguments)
        {
            psi.ArgumentList.Add(argument);
        }
        foreach (var variable in plan.Environment)
        {
            psi.Environment[variable.Key] = variable.Value;
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                onOutput(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                onOutput(args.Data);
            }
        };
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 Java 进程。");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new LaunchProcessResult(process.ExitCode, AnalyzeExitCode(process.ExitCode));
    }

    private static Encoding ResolveEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(0);
    }

    private static string? AnalyzeExitCode(int exitCode) => exitCode switch
    {
        0 => null,
        -1 => "游戏异常退出，请检查任务中心的 Java 日志。",
        1 => "Minecraft 启动失败。常见原因是 Java 版本不匹配、模组冲突或资源文件缺失。",
        _ => $"Minecraft 以退出代码 {exitCode} 结束，请查看日志定位原因。"
    };
}
