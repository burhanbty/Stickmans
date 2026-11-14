using System.Diagnostics;

namespace DesktopStickmanUniverse.Performance;

public sealed record PerformanceSnapshot(
    double Fps,
    double FrameTimeMs,
    double CpuPercent,
    double MemoryMb,
    double WindowScanMs,
    int TargetFps,
    int ActiveOverlays,
    int SkippedIdleRenders);

/// <summary>
/// Measures real process CPU usage, working-set memory, render frame timings,
/// and chooses adaptive frame rates based on character motion and Performance Mode.
/// </summary>
public sealed class PerformanceMonitor
{
    private readonly Process _process = Process.GetCurrentProcess();
    private DateTime _lastCpuSampleUtc = DateTime.UtcNow;
    private TimeSpan _lastProcessorTime;

    private double _smoothedFrameTimeMs;
    private double _smoothedFps;
    private double _cpuPercent;
    private double _memoryMb;
    private int _frameCounter;
    private int _skippedIdleRenders;
    private DateTime _fpsWindowStartUtc = DateTime.UtcNow;

    public PerformanceMonitor()
    {
        _lastProcessorTime = _process.TotalProcessorTime;
    }

    public int ComputeTargetFps(
        bool isPaused,
        bool allMonitorsSuppressed,
        bool performanceMode,
        bool hasActiveMotion,
        bool isDraggingOrFighting)
    {
        if (isPaused || allMonitorsSuppressed)
        {
            return 0;
        }

        if (performanceMode)
        {
            if (isDraggingOrFighting)
            {
                return 36;
            }

            return hasActiveMotion ? 28 : 6;
        }

        if (isDraggingOrFighting)
        {
            return 60;
        }

        return hasActiveMotion ? 48 : 8;
    }

    public void RecordFrame(double frameMs, bool rendered)
    {
        if (rendered)
        {
            _frameCounter++;
        }
        else
        {
            _skippedIdleRenders++;
        }

        _smoothedFrameTimeMs = _smoothedFrameTimeMs <= 0.001
            ? frameMs
            : _smoothedFrameTimeMs * 0.85 + frameMs * 0.15;

        var now = DateTime.UtcNow;
        double fpsElapsed = (now - _fpsWindowStartUtc).TotalSeconds;
        if (fpsElapsed >= 0.5)
        {
            _smoothedFps = _frameCounter / fpsElapsed;
            _frameCounter = 0;
            _fpsWindowStartUtc = now;

            SampleSystemMetrics(now);
        }
    }

    private void SampleSystemMetrics(DateTime nowUtc)
    {
        double wallMs = (nowUtc - _lastCpuSampleUtc).TotalMilliseconds;
        if (wallMs < 400)
        {
            return;
        }

        try
        {
            _process.Refresh();
            TimeSpan totalCpu = _process.TotalProcessorTime;
            double cpuDeltaMs = (totalCpu - _lastProcessorTime).TotalMilliseconds;
            _cpuPercent = Math.Clamp((cpuDeltaMs / (wallMs * Environment.ProcessorCount)) * 100.0, 0.0, 100.0);
            _memoryMb = _process.PrivateMemorySize64 / (1024.0 * 1024.0);

            _lastProcessorTime = totalCpu;
            _lastCpuSampleUtc = nowUtc;
        }
        catch
        {
            // Ignore transient process sampling errors.
        }
    }

    public PerformanceSnapshot GetSnapshot(double windowScanMs, int targetFps, int activeOverlays) => new(
        _smoothedFps,
        _smoothedFrameTimeMs,
        _cpuPercent,
        _memoryMb,
        windowScanMs,
        targetFps,
        activeOverlays,
        _skippedIdleRenders);
}
