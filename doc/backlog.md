# UJMW Backlog

Central list of open design debts, cleanups and features which are not (yet) part of an active Epic.

| Id | Topic | Description | Origin | State |
|---|---|---|---|---|
| TODO-LOG | Consolidation of UJMW log event kinds | UJMW as a whole reserves the event-kind range `72xxx`. The existing log calls scattered over the code base use inline event-kind numbers. These must first be indexed backwards (which number is used where and for what) so that all calls can later be migrated to one UJMW-wide `MyLogEventKind` enum covering `72xxx`. New implementations (e.g. the queue mode) already use dedicated safe sub-ranges (queue mode: `72400` – `72499`). | [epic-async-queue-mode.md](epics/epic-async-queue-mode.md) (Q-LOG) | open |
