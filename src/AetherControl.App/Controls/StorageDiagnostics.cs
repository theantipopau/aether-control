namespace AetherControl.App.Controls;

/// <summary>
/// In-memory-only development toggles, kept around (not deleted) after closing the "680GB/340GB"
/// storage flicker investigation — they earned their place: capturing a live trace with
/// <see cref="TraceEnabled"/> is what proved the root cause (a card recreated every poll) and later
/// confirmed the fix (the same drive holding one card instance across ~90+ seconds of live polling —
/// see ROADMAP.md Phase 32). Deliberately NOT part of the persisted <c>AppSettings</c>/SQLite schema
/// (no migration risk for a diagnostic), and both default OFF for normal operation — flip
/// <see cref="TraceEnabled"/> back on (and rebuild) if a similar "card flickers/wrong value" report
/// ever needs the same kind of pixel-path evidence again.
/// </summary>
internal static class StorageDiagnostics
{
    /// <summary>False (default) is normal operation. True bypasses NumberTween for the Storage
    /// section's MetricCards entirely — see MetricCard.DiagnosticTag — isolating whether the
    /// animation layer is involved in a reported wrong-value sighting versus the value being wrong
    /// before it ever reaches the tween.</summary>
    public static bool AnimationEnabled { get; set; } = true;

    /// <summary>False (default) is normal operation — no tracing overhead, no log growth. True
    /// traces every NumericValue change and tween frame for the Storage section's MetricCards to
    /// ui-value-trace.log — see UiValueTraceLog and MetricCard.DiagnosticTag.</summary>
    public static bool TraceEnabled { get; set; }
}
