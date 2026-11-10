using System;
using System.Diagnostics;
using System.Threading;

namespace Demo {

  /// <summary>
  /// Demo implementation of 'ILongRunningDemoService': simulates work in steps of 500 ms which are grouped
  /// into the stages 'Preparing' (first 20%), 'Processing' and 'Committing' (last 20%).
  /// </summary>
  public class LongRunningDemoService : ILongRunningDemoService {

    private const int _StepDurationMs = 500;
    private const string _StagePreparing = "Preparing";
    private const string _StageProcessing = "Processing";
    private const string _StageCommitting = "Committing";

    /// <summary>
    /// Receives one simulated step (called before the step is executed).
    /// </summary>
    private delegate void StepHandler(string stage, bool stageChanged, int current, int total, bool cancellable);

    /// <summary>
    /// Full variant: reports status text, current and total via 'reportProgress(text, current, total)'.
    /// </summary>
    public string RunWithFullProgress(
      CancellationToken cancellationToken, bool willIgnoreCancel, int executionDurationSec,
      bool willReportStages, bool willReportCurrent, bool willReportTotal,
      Action<string, int?, int?> reportProgress
    ) {

      return this.Execute(
        cancellationToken, willIgnoreCancel, executionDurationSec, false, false,
        (stage, stageChanged, current, total, cancellable) => {

          string text = null;
          int? reportedCurrent = null;
          int? reportedTotal = null;

          if (willReportStages && stageChanged) {
            text = stage;
          }

          if (willReportCurrent) {
            reportedCurrent = current;
          }

          //the total only needs to be reported once (the UI keeps the last state)
          if (willReportTotal && current == 1) {
            reportedTotal = total;
          }

          if (text != null || reportedCurrent.HasValue || reportedTotal.HasValue) {
            reportProgress.Invoke(text, reportedCurrent, reportedTotal);
          }

        }
      );
    }

    /// <summary>
    /// Variant which reports '(total, current)' in the wrong order: 'reportProgress(total, current)'.
    /// </summary>
    public string RunWithSwappedProgress(
      CancellationToken cancellationToken, bool willIgnoreCancel, int executionDurationSec,
      bool willReportStages, bool willReportCurrent, bool willReportTotal,
      Action<int, int> reportProgress, Action<string> reportStageProgress
    ) {

      return this.Execute(
        cancellationToken, willIgnoreCancel, executionDurationSec, false, false,
        (stage, stageChanged, current, total, cancellable) => {

          if (willReportStages && stageChanged) {
            reportStageProgress.Invoke(stage);
          }

          int reportedCurrent = 0;
          int reportedTotal = 0;

          if (willReportCurrent) {
            reportedCurrent = current;
          }

          if (willReportTotal) {
            reportedTotal = total;
          }

          if (willReportCurrent || (willReportTotal && current == 1)) {
            //intentionally swapped!
            reportProgress.Invoke(reportedTotal, reportedCurrent);
          }

        }
      );
    }

    /// <summary>
    /// Variant with a temporarily restricted cancellation: 'reportProgress(text, current, total, cancellable)'.
    /// </summary>
    public string RunWithCancellationRestriction(
      CancellationToken cancellationToken, bool willIgnoreCancel, int executionDurationSec,
      bool willReportStages, bool willReportCurrent, bool willReportTotal,
      Action<string, int?, int?, bool?> reportProgress
    ) {

      bool? lastReportedCancellable = null;

      return this.Execute(
        cancellationToken, willIgnoreCancel, executionDurationSec, true, false,
        (stage, stageChanged, current, total, cancellable) => {

          string text = null;
          int? reportedCurrent = null;
          int? reportedTotal = null;
          bool? reportedCancellable = null;

          if (willReportStages && stageChanged) {
            text = stage;
          }

          if (willReportCurrent) {
            reportedCurrent = current;
          }

          if (willReportTotal && current == 1) {
            reportedTotal = total;
          }

          //the cancellation restriction is always reported when it changes (independent of the flags)
          if (lastReportedCancellable != cancellable) {
            reportedCancellable = cancellable;
            lastReportedCancellable = cancellable;
          }

          if (text != null || reportedCurrent.HasValue || reportedTotal.HasValue || reportedCancellable.HasValue) {
            reportProgress.Invoke(text, reportedCurrent, reportedTotal, reportedCancellable);
          }

        }
      );
    }

    /// <summary>
    /// Long-running variant which fails (fault) after half of the execution duration.
    /// </summary>
    public string RunAndFail(
      CancellationToken cancellationToken, bool willIgnoreCancel, int executionDurationSec,
      bool willReportStages, bool willReportCurrent, bool willReportTotal,
      Action<string, int?, int?> reportProgress
    ) {

      return this.Execute(
        cancellationToken, willIgnoreCancel, executionDurationSec, false, true,
        (stage, stageChanged, current, total, cancellable) => {

          string text = null;
          int? reportedCurrent = null;
          int? reportedTotal = null;

          if (willReportStages && stageChanged) {
            text = stage;
          }

          if (willReportCurrent) {
            reportedCurrent = current;
          }

          if (willReportTotal && current == 1) {
            reportedTotal = total;
          }

          if (text != null || reportedCurrent.HasValue || reportedTotal.HasValue) {
            reportProgress.Invoke(text, reportedCurrent, reportedTotal);
          }

        }
      );
    }

    /// <summary>
    /// Classic synchronous operation (no CancellationToken).
    /// </summary>
    public string RunSynchronously(int executionDurationSec) {

      Stopwatch stopwatch = Stopwatch.StartNew();

      if (executionDurationSec > 0) {
        Thread.Sleep(TimeSpan.FromSeconds(executionDurationSec));
      }

      return "Synchronous execution finished after " + stopwatch.Elapsed.TotalSeconds.ToString("0.0") + " s.";
    }

    /// <summary>
    /// Simulates the work: executes 'executionDurationSec * 2' steps of 500 ms each and calls the handler before every step.
    /// Unless 'willIgnoreCancel' is set, a cancellation request terminates the work immediately
    /// (an 'OperationCanceledException' is thrown, which leads to the execState 'cancelled').
    /// </summary>
    /// <param name="restrictCancellationWhileCommitting">If true, the cancellation is not possible during the stage 'Committing'.</param>
    /// <param name="failHalfway">If true, an exception is thrown after half of the steps.</param>
    private string Execute(
      CancellationToken cancellationToken, bool willIgnoreCancel, int executionDurationSec,
      bool restrictCancellationWhileCommitting, bool failHalfway, StepHandler onStep
    ) {

      Stopwatch stopwatch = Stopwatch.StartNew();

      int total = executionDurationSec * 1000 / _StepDurationMs;
      if (total < 1) {
        total = 1;
      }

      int preparingSteps = total / 5;
      int committingStart = total - (total / 5) + 1;
      string lastStage = null;
      bool cancellationIgnored = false;

      for (int current = 1; current <= total; current++) {

        string stage = _StageProcessing;
        if (current <= preparingSteps) {
          stage = _StagePreparing;
        } else if (current >= committingStart) {
          stage = _StageCommitting;
        }

        bool cancellable = true;
        if (restrictCancellationWhileCommitting && stage == _StageCommitting) {
          cancellable = false;
        }

        bool stageChanged = (stage != lastStage);
        lastStage = stage;

        onStep.Invoke(stage, stageChanged, current, total, cancellable);

        if (failHalfway && current > total / 2) {
          throw new InvalidOperationException("Simulated failure in stage '" + stage + "' at step " + current + " of " + total + ".");
        }

        if (willIgnoreCancel || !cancellable) {

          Thread.Sleep(_StepDurationMs);
          if (cancellationToken.IsCancellationRequested) {
            cancellationIgnored = true;
          }

        } else {

          //waiting on the handle allows an immediate reaction to the cancellation request
          cancellationToken.WaitHandle.WaitOne(_StepDurationMs);
          cancellationToken.ThrowIfCancellationRequested();

        }

      }

      string summary = "Executed " + total + " steps in " + stopwatch.Elapsed.TotalSeconds.ToString("0.0") + " s.";
      if (cancellationIgnored) {
        summary = summary + " A cancellation was requested, but ignored.";
      }

      return summary;
    }

  }

}
