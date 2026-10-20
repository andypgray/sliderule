namespace SlideRule.Cli.Replay;

/// <summary>
///     Thrown by <see cref="LazyCaptureReplaySource" /> when a capture that
///     <see cref="Roslyn.Replay.BinlogCaptureStore" />
///     validated as usable nonetheless fails to replay at runtime — a torn copy, a reference that vanished
///     between validation and materialisation.
/// </summary>
/// <remarks>
///     A recoverable fallback signal, not a user error: its <see cref="System.Exception.Message" /> is already the
///     complete notice, and it never reaches <see cref="CliErrorMapper" />.
/// </remarks>
internal sealed class CaptureReplayFailedException(string notice, Exception inner) : Exception(notice, inner);
