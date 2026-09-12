namespace AetherControl.App.Controls;

/// <summary>
/// Temporary, in-memory-only development toggles for the storage-flicker investigation — deliberately
/// NOT part of the persisted <c>AppSettings</c>/SQLite schema (no migration risk for what's meant to
/// be a short-lived diagnostic), and reset to their normal-operation defaults on every launch. Flip
/// these from Settings while trying to catch the reported "680GB/340GB" display bug in the act; leave
/// them alone otherwise. Remove this whole class once the investigation is closed.
/// </summary>
internal static class StorageDiagnostics
{
    /// <summary>False bypasses NumberTween for the Storage section's MetricCards entirely — see
    /// MetricCard.AnimationEnabled. Isolates whether the animation layer is involved in a reported
    /// wrong-value sighting versus the value being wrong before it ever reaches the tween.</summary>
    public static bool AnimationEnabled { get; set; } = true;

    /// <summary>True traces every NumericValue change and tween frame for the Storage section's
    /// MetricCards to ui-value-trace.log — see UiValueTraceLog and MetricCard.DiagnosticTag.
    /// Temporarily defaulted on while the 680/340 report is being actively investigated — flip back
    /// to false once closed, per "do not ship a permanently cluttered interface" / always-on tracing.</summary>
    public static bool TraceEnabled { get; set; } = true;
}
