# Copilot Instructions

## Project Guidelines
- UJMW long-running operations feature purpose is decoupling the transport layer from HTTP timeouts only (calls stay synchronous for callers, polling is internal, CancellationToken is the only abort channel); general async usage (UShell.ServerCommands, async/await support) is a later, separate step. This feature is implemented only for ASP.NET Core (never WCF); design is tracked in doc/epics/epic-long-running-operations.md.

## General Guidelines
- In this Visual Studio agent environment, prefer file tools (get_file, replace_string_in_file, grep_search) over terminal commands for reading/editing files, as PowerShell terminal calls frequently hang or cancel.