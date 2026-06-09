# EraTranslator Handoff

## Project

- Stack: C# / WPF / .NET 8
- App: `EraTranslator/EraTranslator.csproj`
- Tests: `EraTranslator.Tests/EraTranslator.Tests.csproj`
- EzTransXP worker: `EraTranslator.EzTransWorker/EraTranslator.EzTransWorker.csproj`
- Current app version: `0.7.1`

## Current State

- Translation progress is stored in SQLite at `.era-translator/state.db`.
- Single-row edits use incremental progress saves; bulk operations keep snapshot-style saves.
- Automatic translation progress saves now use row-level incremental upserts for changed items; full snapshots are reserved for phase boundaries, completion, cancel, and other cleanup points.
- Manual edits set `수동 수정`, and duplicate `OriginalText` items are updated together across the full item set, not only the current filter.
- Filtered automatic translation still sends only visible/filter-scoped rows to the provider, but same-original reuse/propagation checks all rows.
- Automatic translation now internally runs in `CSV reference keys -> CSV general -> ERB identifiers -> ERH -> ERB` order inside the current filtered scope, with forced progress saves between phases.
- Phase-boundary glossary hints are rebuilt from current translated items instead of being stored separately; `ERH` can consume `CSV` hints, and `ERB` can consume `CSV + ERH` hints.
- Manual exclusions are persisted and restored as excluded items.
- Translation reset now preserves excluded rows instead of clearing them back into the pending pool.
- Scan-time and translation-time source-language filtering can auto-fill rows whose entire meaningful content is already in the target language, while still applying the normal translation post-processing/validation path.
- Persisted completed/review/manual rows with blank translations are now converted back to a failure state during restore so stop/resume cannot leave empty successful rows behind.
- Provider results with broken placeholder tokens such as missing/reordered `__PH0__` are now terminal `번역 실패` items, not saveable review items.
- Dictionary-first translation is now available for short `ja -> ko` terms before LLM/provider calls, using bundled lexicon lookup, kana/kanji fallback, and optional Naver dictionary lookup.
- Grid live refresh is optional; manual refresh keeps the visible row set stable while editing.
- The main window title is driven by `ApplicationInfo.WindowTitle`.
- The app now opens first and restores persisted project DB state afterward via a startup loading modal only when persisted state actually exists.
- New project path fields start empty by default; sample directory auto-detection is no longer enabled for the normal app constructor.
- Scanner scope is intentionally limited to supported folders `erb`, `csv/cvs`, and `data`, case-insensitively.
- Scanner traversal explicitly skips `.era-translator` and `.era-translator-backup` paths.
- `ERD` is treated as a CSV-like data file and is included in extraction/search filters.
- CSV/ERD file stems under supported folders are registered as dynamic symbol namespaces for the scan session.
- ERB/ERH function and variable identifiers are extracted into dedicated identifier items and rewritten globally after translation if the translated identifier is valid.
- `#DIMS` lookup arrays, split lookup arrays discovered from `SPLIT_STRING`, CSV-name `SELECTCASE`, key-list functions, and adjacent `"namespace","key"` function arguments are treated as symbol references rather than free text.
- Natural angle-bracket print tokens such as `PRINT <愛液>` extract the inner text, while HTML-like ASCII tags such as `<br>` remain protected.
- Result saving is now always effective `ExportCopy`; the output directory must be different from the game directory.
- Team collaboration support is now implemented as a separate FastAPI server under `server/` plus WPF client-side team mode.
- Team mode uses `TeamWorkspaceRoot/<ProjectId>/source` as the game directory and `TeamWorkspaceRoot/<ProjectId>/output` as the output directory after downloading the active server source snapshot.
- Team server authentication uses `/api/auth/login` access tokens in `Authorization: Bearer <token>`. Do not put a project membership `user_id` in the client token field.
- `ClientId` is a device/workspace identifier only. It is registered through `/api/clients/register` and does not replace authentication.
- Shared team keys are based on all `IsReferenceBearingKey` scan items, not a hardcoded CSV list.

## Recent Major Changes

- Bumped app version metadata and title display to `EraTranslator 0.7.1`.
- Added `CHANGELOG.md` and expanded `README.md` with release/workflow documentation.
- Added parallel scanning, throttled scan progress, optimized scan-session SQLite saves, and index-based reference analysis.
- Added `ERH` file support and `#DIM/#DIMS` string extraction.
- Added `DocumentFileTypes` helper for shared ERB/ERH handling.
- Added code-mixed ERB text span extraction while keeping dialogue-like mixed text at sentence level.
- Added same-original translation carryover/reuse so newly extracted duplicate originals inherit existing translations.
- Extended Korean Josa rewriting to regular Korean sentences, reverse parenthesized forms, split-line literal particles, and `%조사선택(...)%` / `%조사만선택(...)%`.
- Added `KoreanParticleClassifier` for shared batchim/numeric particle decisions.
- Added configurable protected full-width characters in the user dictionary window; full-width spaces are no longer protected by default.
- Added extraction and translation completion alert dialogs.
- Improved automatic translation same-original propagation so filtered translation runs reuse completed translations and update matching pending rows outside the current filter.
- Added code-mixed span output replacement tests and ensured surrounding ERB code remains intact.
- Moved EzTransXP worker output under `workers/EzTransXP/` to avoid confusing users with a second root exe.
- Added EzTransXP worker path fallback for older layouts.
- Reworked progress persistence into snapshot and row-level update paths.
- Added row-level SQLite upsert/delete APIs for edited items.
- Fixed lower editor and DataGrid commit paths so manual edits save once and update status correctly.
- Added `함수 교정` and 조사 처리 buttons near the refresh controls.
- Added function/expression filter support.
- Added function correction for full-width commas inside function calls and brace expressions.
- Added save-time ERB reference re-extraction so stale persisted `symbol_references` do not block rewrites.
- Expanded ERB reference extraction for expression/variable indexes, aliases, and semicolon-terminated references.
- Added symbol namespace aliases: `BASE <-> MAXBASE`, `ITEM <-> ITEMPRICE`, `PALAM <-> JUEL`.
- Added numeric-safe reference output for translated keys that are unsafe in ERB symbolic syntax.
- Fixed export-copy to copy unchanged game files while excluding translator state/backup folders.
- Fixed bundled ZNAME output encoding and removed per-file `#INCLUDE "ZNAME.ERH"` insertion.
- Updated `GameBase.csv` extraction so column 0 keys are not translated.
- Expanded `README.md` with feature descriptions, button descriptions, and recommended workflow.
- Added `.codex-tmp/` and `release/` to `.gitignore`.
- Disabled default sample folder population so release builds start with empty game/output folders.
- Confirmed pre-running `조사처리` does not duplicate Josa conversion on save; added an output writer regression test.
- Changed automatic Josa rewriting so pass-through particles `의` and `에게` are not converted from postfix/split forms. Explicit existing `%조사처리(...,"의")%` remains supported for compatibility.
- Reworked LM Studio translation requests to prefer `json_schema` structured output, retry once with stricter settings, and only then fall back to the tokenized pipe protocol.
- Added LM Studio advanced sampling options: `TopP`, `TopK`, `RepeatPenalty`, `PresencePenalty`, `Seed`, and `MaxTokens`.
- Added model-family-aware LM Studio/Lemonade presets, including selectable `자동`, `Gemma 4`, `Qwen 3.5 9B`, `TranslateGemma`, `Hy-MT2 7B`, and `Hy-MT2 30B-A3B` profiles.
- Added LM Studio/Qwen/Gemma thinking control support with `enable_thinking=false` custom-field requests and parser-side stripping of leaked thought/reasoning blocks.
- Added `PromptProfile` selection with `자동`, `기본`, and `Hy-MT2` prompt defaults for OpenAI-compatible providers. `TranslateGemma` remains a dedicated request path and intentionally bypasses user prompt templates.
- Added `TranslateGemma` dedicated request mode using the model-specific message payload shape, single-item batching, and plain-text response handling.
- Added `Lemonade` provider support with `/v1/models` model loading and provider-specific OpenAI-compatible parameter filtering.
- Added `XiaomiMiMo` cloud provider support with static recommended model options, `thinking.type`, and `max_completion_tokens` request mapping.
- Added prompt-side glossary hint injection for OpenAI-compatible providers and batch-level overlap selection.
- Added translation option reset buttons per tab, fixed decimal input handling for sampling fields, and changed result-state logging defaults to `false`.
- Changed the default for `원문이 소스 언어가 아니면 자동 제외` / `ExcludeNonSourceText` to `true` for new configs and translation-option resets.
- Fixed the translation settings window so pressing `Esc` closes the dialog instead of resetting the provider selection.
- Reworked translation prompt composition so LM Studio tokenized fallback now uses the same user-editable prompt templates as JSON/schema modes.
- Split prompt composition into shared translation constraints plus format-specific rules to reduce duplicated instructions in system prompts.
- Added confirmation dialogs before `번역 리셋` and `추출 리셋` so destructive resets require explicit user confirmation.
- Tightened automatic translation validation so blank normalized output, Japanese leakage in `ja -> ko`, and unchanged Japanese-origin output such as kanji-only source echoes are marked as failures instead of successful translations.
- Recalculated the top-right warning summary from current item state so translation reset clears review/failure counts immediately.
- Added bundled Japanese lexicon snapshot assets and dictionary-first translation services for short `ja -> ko` terms before provider calls.
- Added katakana transliteration fallback, kanji reading fallback, and optional Naver Japanese dictionary lookup with local persistence.
- Added dictionary-hit logging and translation-setting options for bundled dictionary, Naver lookup, fallback toggles, and max dictionary-first term length.
- Added user dictionary apply modes `치환` and `프롬프팅`, with prompting used for LLM-style providers and replace fallback used for non-LLM providers.
- Added dynamic CSV/ERD namespace discovery from supported folder file stems, including non-ASCII namespace parsing for direct references and `GETNUM`.
- Added custom namespace reference rewrite for forms such as `namespace:key`, `namespace:ARG:key`, `namespace:(expr):key`, and `GETNUM(namespace, "key")` / `GETNUM(namespace, @"key")`.
- Changed unresolved dynamic references from namespace-wide save blockers into warnings; only resolved indirect references without a rewrite location remain blocking.
- Added `ERD` extraction and filtering with CSV-like handling.
- Added `ErbIdentifierExtractor`, identifier scan persistence, `ERB-식별자` translation phase, and `IdentifierRewritePlanner`.
- Added identifier translation validation: whitespace is removed, invalid code characters fail, and collisions/invalid outputs keep original code safe.
- Added extraction guards for comments, resource paths, `LOADTEXT`/`SAVETEXT`, palette keys, calculation rule strings, DIMS lookup arrays, CSV-name `SELECTCASE`, and code-only expressions.
- Added startup persisted-state gating plus a non-closable loading modal while DB restore is in progress.
- Added same-original manual status propagation so manual status changes update matching originals together.
- Added output-folder validation that rejects using the game folder as the output folder.
- Reworked backup creation into unique zip archives and excluded translator state/backup paths from scanning.
- Hid the `함수 교정` button from the main UI while keeping the underlying feature code available.
- Added DIMS lookup and CSV name `SELECTCASE` reference tracking so translated CSV/DIMS keys rewrite exact code references without free-text translating lookup keys.
- Added split lookup array tracking for `SPLIT_STRING(array:index, delimiter, parts)` patterns so key fields in packed `#DIMS` arrays are rewritten from the primary CSV namespace.
- Added adjacent namespace/key argument reference tracking for calls such as `DISPLAY_FALLEN_PARTS(charaIndex, "EXP", "絶頂経験", ...)`.
- Added output rewrite aliases for `ITEMSALES`, `CUP`, `NOWEX`, `CDOWN`, and `DOWNBASE`.
- Added safer ERB print-tail extraction for display labels, inline conditionals, `%... + "text"%` fragments, and multi-field labels while preserving placeholders.
- Added natural angle-bracket print-tail extraction so `PRINT <愛液>` contributes `愛液` without treating it as HTML markup.
- Fixed percent-placeholder protection so `%...%` ranges cannot leak across lines during save-time preservation checks.
- Added `ERD` and identifier occurrence state to scan snapshot save/restore.
- Changed token mismatch handling in automatic translation so final placeholder damage becomes `번역 실패` with `CanSave=false`.
- Optimized automatic translation progress persistence to save changed rows rather than full snapshots after every provider batch.
- Replaced the old team package note with a TODO plan for a FastAPI-based multi-project team collaboration server.
- Improved same-original automatic translation reuse so conflicting existing automatic translations are not picked arbitrarily, while resolved automatic translations still synchronize matching rows outside the current filter unless they are manual edits or manual exclusions.
- Added the FastAPI team server project under `server/` with SQLAlchemy 2.x, Alembic, Pydantic v2, PostgreSQL configuration, source archive storage, and Windows/Linux run scripts.
- Added server API routes for auth, projects, memberships, assignments, source snapshots, scan manifests, sync, submit, conflicts, and shared keys.
- Added server-rendered admin UI for first-run setup, login, users, projects, memberships/assignments, source snapshots, manifests, shared keys, conflicts, and submission history.
- Added WPF client team mode models/services: project contexts, team project state, server DTOs/client, source sync, scan manifest builder, collaboration sync/submit, and offline submission queue state.
- Added a dedicated team settings window with local/team mode switching, server URL, project list refresh, selected project ID, display name, auth token, workspace root, and client ID.
- Changed team project selection so the combo box drives `TeamProjectId` by default, while manual project ID entry is explicitly opt-in.
- Added tests for project context creation, team state persistence, source sync, scan manifest generation, team collaboration apply/submit, and repeated `TeamServerClient` project refresh calls.
- Fixed the team server/client auth mental model: the WPF auth token field expects an API access token, while server-side project membership uses the user resolved from that token.

## Important Files

- UI/ViewModel: `EraTranslator/MainWindow.xaml`, `EraTranslator/MainWindow.xaml.cs`, `EraTranslator/ViewModels/MainWindowViewModel.cs`
- Team UI: `EraTranslator/TeamSettingsWindow.xaml`, `EraTranslator/TeamSettingsWindow.xaml.cs`
- Version info: `EraTranslator/ApplicationInfo.cs`, `EraTranslator/EraTranslator.csproj`
- Persistence: `EraTranslator/Services/SqliteProjectStateStore.cs`, `EraTranslator/Services/ProjectStatePersistenceService.cs`
- Team client services: `EraTranslator/Services/ProjectContextFactory.cs`, `EraTranslator/Services/TeamServerClient.cs`, `EraTranslator/Services/TeamSourceSyncService.cs`, `EraTranslator/Services/TeamScanManifestBuilder.cs`, `EraTranslator/Services/TeamCollaborationService.cs`, `EraTranslator/Services/TeamProjectStateService.cs`
- Team models: `EraTranslator/Models/ProjectMode.cs`, `EraTranslator/Models/ProjectContext.cs`, `EraTranslator/Models/TeamProjectState.cs`, `EraTranslator/Models/TeamScanManifest.cs`, `EraTranslator/Models/TeamServerDtos.cs`
- Team server: `server/app/main.py`, `server/app/api/routes/`, `server/app/web/router.py`, `server/app/models/`, `server/alembic/versions/`, `server/scripts/`
- Extraction: `EraTranslator/Services/ErbExtractor.cs`, `EraTranslator/Services/ErbReferenceExtractor.cs`, `EraTranslator/Services/ErbIdentifierExtractor.cs`, `EraTranslator/Services/ErbDimsLookupRegistry.cs`, `EraTranslator/Services/CsvSchemaClassifier.cs`
- Save/rewrite: `EraTranslator/Services/OutputWriter.cs`, `EraTranslator/Services/SymbolRewritePlanner.cs`, `EraTranslator/Services/IdentifierRewritePlanner.cs`, `EraTranslator/Services/InlineSymbolReferenceRewriter.cs`
- Quality rules: `EraTranslator/Services/TranslationQualityRules.cs`, `EraTranslator/Services/TranslationPromptTemplates.cs`, `EraTranslator/Services/PhaseScopedGlossaryBuilder.cs`
- Shared file/particle helpers: `EraTranslator/Services/DocumentFileTypes.cs`, `EraTranslator/Services/KoreanParticleClassifier.cs`
- Provider stack: `EraTranslator/Services/OpenAiCompatibleTranslationProvider.cs`, `EraTranslator/Services/DictionaryFirstTranslationService.cs`, `EraTranslator/Services/LmStudioSamplingDefaults.cs`, `EraTranslator/Services/ModelCatalogService.cs`, `EraTranslator/Services/EzTransXpTranslationProvider.cs`, `EraTranslator/Services/EzTransXpWorkerProcessClient.cs`
- Release notes: `CHANGELOG.md`
- Saved plans: `docs/plans/team-translation-support-plan.md`, `docs/plans/team-translation-server-plan.md`

## Behavior Notes

- EzTransXP is still a separate x86 worker process because it loads native EzTransXP DLLs.
- The worker should be distributed under `workers/EzTransXP/`; a root-level worker exe is only a fallback.
- Build request conventions:
  - `디버그 빌드해줘` -> build to `EraTranslator/bin/Debug/net8.0-windows`
  - `릴리즈 빌드해줘` -> build to `EraTranslator/bin/Release/net8.0-windows`
  - `출시해줘` -> build first, then organize artifacts under `release/` and create the release zip
  - If the target output is locked by a running binary, do not create a temporary build folder; skip that build/release step and report the lock instead
- Scanner support is intentionally folder-scoped: only `erb`, `csv/cvs`, and `data` directories are scanned, and collected paths are de-duplicated by absolute path.
- `#DIM` and `#DIMS` string literals are extracted for ERB-like files, including `ERH`.
- ERB/ERH identifier items are not natural-language text. Their translations must normalize to non-empty, whitespace-free, code-safe identifiers before rewrite.
- Short hiragana particle/inflection tokens such as `た`, `から`, `まで`, `なら`, and `した` should not become identifier items; they are only translated when part of natural text segments.
- `TCVAR:`, `CSTR:`, CSV/ERD namespaces, `LOADTEXT`, `SAVETEXT`, resource path functions, palette lookup keys, and protected rule strings should stay protected from free-text translation and identifier rewrite.
- Dynamic CSV key construction like `GETNUM(CSTR, "記録：" + keyword)` remains a risky pattern unless the keyword argument is also mapped to the same CSV key convention. If the prefix or keyword is translated inconsistently, `GETNUM` can return `-1`.
- For packed lookup arrays, prefer the primary CSV file's translated key over duplicate ERB split-field translations when both represent the same original key.
- Adjacent namespace/key function arguments should use the normal symbol-key normalization path; for example a translated `EXP` key with spaces should become a code-safe symbol key before output.
- Natural angle-bracket text in print tails should be extracted only when the inner token is natural Japanese text; do not treat actual HTML/resource tags as free text.
- `GameBase.csv` column 0 is intentionally skipped; column 1 remains translatable.
- Save-time reference re-extraction means users should not need to rescan just because the persisted ERB reference cache is stale.
- CSV/ERD stem namespaces are dynamic per scan session. Built-in aliases still take priority, then dynamic namespace fallback applies.
- Unresolved dynamic references should warn, not block saving whole namespaces. Keep blocking only when a resolved indirect reference cannot be mapped to an output replacement location.
- `GETNUM(namespace, expr)` is only rewriteable when `expr` can be resolved to a literal.
- Automatic Josa rewrite should handle particles that require 받침 selection. Pass-through particles such as `의` and `에게` should remain as plain text unless the source already explicitly uses a Josa helper.
- Protected full-width characters default to `／【】＜＞「」（）『』％：` and are configurable in `사용자 사전...` -> `보호 문자`.
- Full-width spaces (`　`) intentionally remain unprotected.
- Bundled dictionary assets live under `Assets/Dictionaries/` and include the `bundled-japanese-lexicon.sqlite` snapshot plus the EDRDG notice file.
- Dictionary-first translation is intentionally limited to short eligible `ja -> ko` terms and should not run for long sentence-like text, code-only text, or character-sheet name values.
- Filtered translation runs should not create divergent translations for identical originals: existing completed translations win over new provider requests.
- Glossary hints are derived from translated `CSV`/`ERH` items at phase boundaries and are not persisted as standalone project data.
- Automatic translation hot path should persist changed items through incremental upsert; avoid reintroducing per-batch full snapshots.
- Placeholder token validation failure is a translation failure and should remain retryable; do not downgrade it to a saveable review item.
- LM Studio structured-output mode now logs response-mode metadata such as `json_schema`, `json_schema_retry`, and `tokenized_fallback`.
- Translation option resets should restore model-appropriate LM Studio defaults, not a single fixed preset.
- Auto LM Studio/Lemonade preset application should also update `MaxTokens`, especially for `Hy-MT2` presets.
- LM Studio tokenized fallback should inherit `SystemPromptTemplate` / `RetryPromptTemplate`; it no longer uses a separate hardcoded system prompt path.
- `TranslateGemma` uses a dedicated payload path and enforces effective batch size `1`; glossary hints and user-editable prompt templates do not apply to that path.
- `XiaomiMiMo` is treated as a cloud OpenAI-compatible provider with static recommended models instead of live model catalog loading.
- Lemonade should only receive parameters documented by the server; unsupported LM Studio-only fields such as `enable_thinking` or `json_schema` should not be sent there.
- Generated folders such as `.codex-tmp/` and `release/` are local artifacts and should not be committed.
- Files and folders listed in `.gitignore` are local-only by policy and must not be staged or committed.
- Release zip packaging should exclude `EraTranslator.config.json`, `.pdb`, logs, caches, and local state folders, and should keep the EzTransXP worker under `workers/EzTransXP/`.
- Startup loading should only show when persisted project state files actually exist under the effective project data directory.
- Output-folder-only save mode is intentional even if older configs still contain `InPlaceWithBackup`.
- Team mode should continue to support fully local work as a separate path; do not make server settings mandatory for local projects.
- Team mode source sync should work even when the user has no original game files locally, as long as the server has an active source snapshot.
- In team mode, the client token field must contain an API access token returned by `/api/auth/login`; server `user_id` values are internal DB identifiers and are not valid Bearer tokens.
- Admin users can see all projects; non-admin users only see projects where their token-resolved user has an active membership.
- `TeamServerClient` deliberately avoids mutating `HttpClient.BaseAddress` or `DefaultRequestHeaders` after construction. Keep requests as absolute URIs with per-request Authorization headers.
- PostgreSQL deployments use the configured schema, default `eratranslator`; request sessions set the search path so raw table names resolve correctly.
- Current team mode gap: assignment/out-of-scope items are rejected on submit by the server, but the WPF grid does not yet mark unassigned items read-only because sync responses do not expose editable flags yet.

## Validation

- Latest full test command:

```powershell
dotnet test .\EraTranslator.Tests\EraTranslator.Tests.csproj
```

- Latest passing result in this thread: `501/501`.
- Latest debug app build in this thread:

```powershell
dotnet build .\EraTranslator\EraTranslator.csproj -c Debug --no-restore
```

- Latest debug build result: success, `0` warnings, `0` errors.
- Latest server test result in this thread: `38/38` passing under `server`.
- Build/release may fail if `EraTranslator.exe` is currently running and locking the output path. In that case, do not fall back to a temporary output folder; skip and report the lock.

## Suggested Next Work

- Consider adding a release packaging target that outputs only user-facing files plus internal worker folders.
- Add visible version information to an About dialog if one is introduced later.
- Add a client-side username/password login flow so users do not have to manually paste `/api/auth/login` access tokens into the team settings window.
- Extend sync responses with assignment/editability metadata and make out-of-scope team items read-only in the WPF grid.
