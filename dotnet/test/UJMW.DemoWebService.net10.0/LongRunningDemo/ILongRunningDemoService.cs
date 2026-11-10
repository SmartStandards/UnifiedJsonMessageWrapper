using System;
using System.Threading;

namespace Demo {

  /// <summary>
  /// Demonstrates UJMW long-running operations (POST ?wait / GET polling / DELETE cancellation) including
  /// progress callbacks which follow the convention-based progress display (callback name contains 'progress',
  /// first string = status text, first int = current, second int = total, first bool = cancellation possible).
  /// Every variant can be tuned via the same parameters:
  /// 'willIgnoreCancel' (the implementation ignores the CancellationToken),
  /// 'executionDurationSec' (overall duration of the simulated work),
  /// 'willReportStages' / 'willReportCurrent' / 'willReportTotal' (which values are reported).
  /// </summary>
  public interface ILongRunningDemoService {

    /// <summary>
    /// Full variant: reports status text, current and total via 'reportProgress(text, current, total)'.
    /// Values which should not be reported are transported as null (the UI keeps their last state).
    /// If all 'willReport...' flags are false, no progress is reported at all (the UI shows the default animation).
    /// </summary>
    /// <param name="cancellationToken">Triggered by a DELETE request of the client.</param>
    /// <param name="willIgnoreCancel">If true, the implementation ignores the cancellation request and runs to the end.</param>
    /// <param name="executionDurationSec">Overall duration of the simulated work in seconds.</param>
    /// <param name="willReportStages">If true, the stage name is reported as status text.</param>
    /// <param name="willReportCurrent">If true, the current step number is reported.</param>
    /// <param name="willReportTotal">If true, the total number of steps is reported.</param>
    /// <param name="reportProgress">Progress callback (text, current, total).</param>
    /// <returns>A summary of the execution.</returns>
    string RunWithFullProgress(
      CancellationToken cancellationToken, bool willIgnoreCancel, int executionDurationSec,
      bool willReportStages, bool willReportCurrent, bool willReportTotal,
      Action<string, int?, int?> reportProgress
    );

    /// <summary>
    /// Variant which reports '(total, current)' in the wrong order: 'reportProgress(total, current)'.
    /// Demonstrates the swap rule of the convention (if total &gt; 1 and total &lt; current, both are swapped).
    /// </summary>
    /// <param name="cancellationToken">Triggered by a DELETE request of the client.</param>
    /// <param name="willIgnoreCancel">If true, the implementation ignores the cancellation request and runs to the end.</param>
    /// <param name="executionDurationSec">Overall duration of the simulated work in seconds.</param>
    /// <param name="willReportStages">If true, the stage name is reported via 'reportStageProgress'.</param>
    /// <param name="willReportCurrent">If true, the current step number is reported (otherwise 0).</param>
    /// <param name="willReportTotal">If true, the total number of steps is reported (otherwise 0).</param>
    /// <param name="reportProgress">Progress callback with swapped arguments (total, current).</param>
    /// <param name="reportStageProgress">Second progress callback which only carries the status text.</param>
    /// <returns>A summary of the execution.</returns>
    string RunWithSwappedProgress(
      CancellationToken cancellationToken, bool willIgnoreCancel, int executionDurationSec,
      bool willReportStages, bool willReportCurrent, bool willReportTotal,
      Action<int, int> reportProgress, Action<string> reportStageProgress
    );

    /// <summary>
    /// Variant with a temporarily restricted cancellation: 'reportProgress(text, current, total, cancellable)'.
    /// During the stage 'Committing' the cancellation is reported as currently not possible
    /// (and the CancellationToken is not evaluated during this stage).
    /// </summary>
    /// <param name="cancellationToken">Triggered by a DELETE request of the client.</param>
    /// <param name="willIgnoreCancel">If true, the implementation ignores the cancellation request and runs to the end.</param>
    /// <param name="executionDurationSec">Overall duration of the simulated work in seconds.</param>
    /// <param name="willReportStages">If true, the stage name is reported as status text.</param>
    /// <param name="willReportCurrent">If true, the current step number is reported.</param>
    /// <param name="willReportTotal">If true, the total number of steps is reported.</param>
    /// <param name="reportProgress">Progress callback (text, current, total, cancellable).</param>
    /// <returns>A summary of the execution.</returns>
    string RunWithCancellationRestriction(
      CancellationToken cancellationToken, bool willIgnoreCancel, int executionDurationSec,
      bool willReportStages, bool willReportCurrent, bool willReportTotal,
      Action<string, int?, int?, bool?> reportProgress
    );

    /// <summary>
    /// Long-running variant which fails (fault) after half of the execution duration.
    /// </summary>
    /// <param name="cancellationToken">Triggered by a DELETE request of the client.</param>
    /// <param name="willIgnoreCancel">If true, the implementation ignores the cancellation request.</param>
    /// <param name="executionDurationSec">Overall (planned) duration of the simulated work in seconds.</param>
    /// <param name="willReportStages">If true, the stage name is reported as status text.</param>
    /// <param name="willReportCurrent">If true, the current step number is reported.</param>
    /// <param name="willReportTotal">If true, the total number of steps is reported.</param>
    /// <param name="reportProgress">Progress callback (text, current, total).</param>
    /// <returns>Never returns regularly.</returns>
    string RunAndFail(
      CancellationToken cancellationToken, bool willIgnoreCancel, int executionDurationSec,
      bool willReportStages, bool willReportCurrent, bool willReportTotal,
      Action<string, int?, int?> reportProgress
    );

    /// <summary>
    /// Classic synchronous operation (no CancellationToken): executed within a single POST request,
    /// can not be cancelled and is therefore bound to the HTTP timeouts.
    /// </summary>
    /// <param name="executionDurationSec">Duration of the simulated work in seconds.</param>
    /// <returns>A summary of the execution.</returns>
    string RunSynchronously(int executionDurationSec);

  }

}
