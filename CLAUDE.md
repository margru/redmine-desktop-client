# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows Forms desktop client (C#, .NET Framework 4.5.1) for tracking and reporting time
spent on issues in a [Redmine](https://www.redmine.org/) project-management server. The core
loop: pick a project + issue + activity, run a ticking timer, then commit the elapsed time as a
Redmine time entry. It also offers full issue CRUD against the Redmine REST API.

## Build & run

This is an MSBuild solution — there is no `dotnet`/npm/test tooling. Build with Visual Studio
2014+ or MSBuild from a Developer/VS command prompt.

```powershell
# 1. The Redmine API is a git submodule and is NOT checked out by default — it must be
#    initialized or the Redmine.Client project will fail to compile (unresolved
#    Redmine.Net.Api references).
git submodule update --init --recursive

# 2. Build the whole solution
msbuild RedmineClient.sln /p:Configuration=Debug /p:Platform="Any CPU"

# Output: Redmine.Client\bin\Debug\RedmineClient.exe  (run it directly to launch the app)
```

Notes:
- There is **no automated test suite** in this repo. Verification is manual: run
  `RedmineClient.exe` and exercise the UI against a Redmine server.
- `Setup` (WiX, builds the MSI) and `Installer` (WiX, builds the bootstrapper bundle) only build
  under the `x86` platform — see the platform mappings in `RedmineClient.sln`. The main client
  builds as `Any CPU`. Build the installer projects only when producing a release.
- The `redmine-net451-api` submodule project is referenced via `ProjectReference`, so it is
  compiled from source as part of the solution build (not a NuGet package).

## Architecture

Three projects (see `RedmineClient.sln`):
- **Redmine.Client** — the WinForms app (all the code you will normally touch).
- **Redmine.Api** — git submodule (`github.com/zapadi/redmine-net-api`); provides
  `Redmine.Net.Api.RedmineManager` and the `Redmine.Net.Api.Types.*` model classes (`Issue`,
  `Project`, `TimeEntry`, `User`, etc.) used throughout the client.
- **Setup** / **Installer** — WiX packaging projects.

### Key pieces in Redmine.Client

- **`RedmineClientForm`** (`RedmineClientForm.cs`, ~1700 lines) — the main window and the heart of
  the app. Implemented as a **singleton** (`RedmineClientForm.Instance`, see `Program.cs`). Holds
  the single shared `static RedmineManager redmine` connection, the ticking timer state, the
  current filter, and the loaded issue grid. Most features live here as event handlers.

- **API connection & versioning** — `RedmineClientForm.redmine` is the one `RedmineManager`
  instance. The target server version is stored as the `ApiVersion` enum (`ClientProject.cs` /
  exposed as `RedmineClientForm.RedmineVersion`). **Feature availability is gated on this version**
  throughout the code (e.g. `if (RedmineVersion >= ApiVersion.V13x)`), because older Redmine
  servers lack endpoints like project trackers, project memberships, custom fields, or the
  priorities/activities enumerations. When adding API calls, guard them with the appropriate
  minimum `ApiVersion`.

- **Background work** — all blocking API calls go through the `BgWorker` base class
  (`BgWorker.cs`), which `RedmineClientForm` extends. `AddBgWork(name, RunAsync)` enqueues a job;
  jobs run one-at-a-time on a `BackgroundWorker`. A job is a `RunAsync` delegate that does the work
  off the UI thread and **returns an `OnDone` delegate** which is then invoked back on the UI
  thread to apply results. Keep API/network work inside the `RunAsync` body and all control updates
  inside the returned `OnDone`.

- **`MainFormData`** (`MainFormData.cs`) — loads everything the main grid/filters need for a given
  project (projects, issues, trackers, statuses, categories, versions, members, priorities,
  activities) in one constructor, throwing `LoadException` (wrapping the underlying error with a
  localized action name) on failure. This is the object built inside a `BgWork` job. `Filter`
  (in `ClientProject.cs`) is the cloneable struct of active filter criteria translated into Redmine
  query parameters here.

- **`Enumerations`** (`Enumerations.cs`) — issue priorities and time-entry activities are not
  reliably available from older APIs, so they are cached locally (XML-serialized) and editable in
  the UI (`EditEnumForm` / `EditEnumListForm`). Newer servers (`>= V22x`) refresh them from the
  API.

- **Settings** — user config (Redmine URL, credentials, language, cache lifetime, window size,
  last-used project/issue/activity, ticking state, etc.) uses the standard .NET
  `Properties.Settings` mechanism. `LoadConfig()` / `SaveRuntimeConfig()` in `RedmineClientForm`
  read and persist them. `Reinit()` re-applies config after the settings dialog changes anything.

- **Forms** — one `*Form.cs` + `*.Designer.cs` per dialog (`IssueForm` for issue create/edit,
  `TimeEntryForm`/`TimeEntriesForm` for time entries, `CommitForm` to log ticked time,
  `SettingsForm`, `AttachmentForm`, `OpenSpecificIssueForm`, `UpdateIssueNoteForm`,
  `IssueGridSelectColumns`). `DialogType` (`Program.cs`) distinguishes New vs Edit reuse of a form.

### Localization

The UI is fully localized via `Languages\Lang.resx` (+ per-culture `Lang.<culture>.resx`:
de, fr, nl, pl, ru, cs-CZ, pt-BR, es-MX, zh-CN, gl). **User-facing strings must go through
`Lang.*` resources, not hardcoded literals.** `LangTools.UpdateControlsForLanguage(...)`
(`Languages\LangTools.cs`) walks a form's control tree and assigns text by matching each control's
`Name` to a resource key — so a control's localized caption comes from a resx entry whose key
equals the control name. `LangTools` also builds version labels (`ApiVersion_*` keys) and field
change-log text (`IssueField_*` keys). The active culture is `Lang.Culture`, set from the
`LanguageCode` setting.

## Conventions

- Mono compatibility is considered in places (`IsRunningOnMono()` branches in
  `RedmineClientForm`); don't assume Windows-only APIs without a fallback if editing those paths.
- New blocking/network operations should be wrapped in `AddBgWork`, never run synchronously on the
  UI thread.
- When touching anything that calls the Redmine API, check whether the endpoint needs an
  `ApiVersion` guard.
