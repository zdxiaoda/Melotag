namespace Melotag.Core.Tests;

using System.Diagnostics;

/// <summary>
/// xUnit v2（本项目 2.9.3，没有 v3 的 <c>Assert.Skip</c>）的跳过方案：
/// 派生特性在<b>测试发现期</b>探测 ffmpeg，缺失时给 <c>Skip</c> 赋值，
/// 结果记为「已跳过」而不是像 <c>return</c> 那样冒充通过。
/// </summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (!Ffmpeg.IsAvailable) Skip = Ffmpeg.SkipReason;
    }
}

/// <summary>与 <see cref="FfmpegFactAttribute"/> 相同，用于 [InlineData] 的参数化用例。</summary>
public sealed class FfmpegTheoryAttribute : TheoryAttribute
{
    public FfmpegTheoryAttribute()
    {
        if (!Ffmpeg.IsAvailable) Skip = Ffmpeg.SkipReason;
    }
}

/// <summary>ffmpeg 探测与测试音频生成；只在依赖外部二进制的用例里使用。</summary>
public static class Ffmpeg
{
    public const string SkipReason = "ffmpeg not found";

    public static string? Path { get; } = FindOnPath("ffmpeg.exe") ?? FindOnPath("ffmpeg");

    public static bool IsAvailable => Path is not null;

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = System.IO.Path.Combine(dir.Trim('"'), fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>用 ffmpeg 生成 0.5 秒静音样本；失败返回 false（调用方直接断言失败，不当作跳过）。</summary>
    public static bool TryCreateSample(string output, string extension)
    {
        if (Path is not { } ffmpeg) return false;

        var codec = extension switch
        {
            ".mp3" => "libmp3lame",
            ".flac" => "flac",
            _ => null,
        };
        if (codec is null) return false;

        var args = $"-hide_banner -loglevel error -y -f lavfi -i anullsrc=r=44100:cl=stereo -t 0.5 -c:a {codec} \"{output}\"";
        using var process = Process.Start(new ProcessStartInfo(ffmpeg, args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        if (process is null) return false;
        process.WaitForExit(30_000);
        return process.ExitCode == 0 && File.Exists(output);
    }
}
