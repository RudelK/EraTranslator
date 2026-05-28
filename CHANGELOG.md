# Changelog

## 0.6 - 2026-05-29

- Improved extraction throughput with parallel file scanning, throttled progress updates, optimized SQLite session saves, and faster reference analysis.
- Added `ERH` support, including extraction from `#DIM` and `#DIMS` string declarations.
- Expanded ERB mixed code/text extraction so reusable labels are extracted as stable spans while dialogue-like mixed text remains sentence-level.
- Improved translation carryover and same-original propagation, including filtered translation runs that reuse completed translations and propagate results to matching rows outside the current filter.
- Expanded Korean particle correction for regular Korean sentences, split-line particles, reverse parenthesized forms, and additional Josa helper forms.
- Added configurable full-width protected characters in the user dictionary window, while leaving full-width spaces unprotected by default.
- Added completion alerts for extraction and translation runs.
- Improved save-time behavior for Josa rewriting, placeholder preservation, and code-mixed span replacement.

## 0.5 - 2026-05-27

- Added visible app version metadata and `EraTranslator 0.5` window title.
- Improved SQLite progress persistence, row-level progress updates, manual edit propagation, and reference rewriting.
- Added function/expression filtering, Josa rewrite controls, and ERB function correction workflows.
