namespace musicApp.Helpers;

/// <summary>Maps system resource snapshots to parallel scan degree (smoothed across batches).</summary>
public static class ScanConcurrencyAdvisor
{
    private const int MaxParallelismConservative = 8;
    private const int MaxParallelismAggressive = 16;
    private const double AvailableRamFractionComfort = 0.12;
    private const ulong AbsoluteMinAvailableRamBytes = 512UL * 1024 * 1024;
    private const double CpuBusyHigh = 88.0;
    private const double CpuBusyLow = 48.0;
    private const double CpuGreenZoneMax = 54.0;

    public static int Recommend(
        SystemResourceSnapshot snapshot,
        int processorCount,
        ref int previousSmoothedDop)
    {
        var totalRam = snapshot.TotalRamBytes;
        var availRam = snapshot.AvailableRamBytes;
        var memComfort = totalRam == 0
            ? true
            : availRam >= AbsoluteMinAvailableRamBytes &&
              (double)availRam / totalRam >= AvailableRamFractionComfort;

        var tierMax = memComfort && snapshot.CpuBusyPercent <= CpuGreenZoneMax
            ? MaxParallelismAggressive
            : MaxParallelismConservative;

        // Reserve one logical CPU for the UI thread so the window stays responsive during scans.
        var reserved = Math.Max(1, processorCount - 1);
        var maxCap = Math.Clamp(reserved, 1, tierMax);
        var raw = maxCap;

        if (!memComfort)
            raw = Math.Max(1, raw / 2);

        if (snapshot.CpuBusyPercent >= CpuBusyHigh)
            raw = Math.Max(1, raw / 2);
        else if (snapshot.CpuBusyPercent <= CpuBusyLow && memComfort)
            raw = maxCap;

        raw = Math.Clamp(raw, 1, maxCap);

        if (previousSmoothedDop <= 0)
            previousSmoothedDop = raw;
        else
            previousSmoothedDop = Math.Max(1, (raw + previousSmoothedDop + 1) / 2);

        return previousSmoothedDop;
    }
}
