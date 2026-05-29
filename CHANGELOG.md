# Changelog

## 0.6 - 2026-05-29

- Improved extraction throughput with parallel file scanning, throttled progress updates, optimized SQLite session saves, and faster reference analysis.
- Added `ERH` support, including extraction from `#DIM` and `#DIMS` string declarations.
- Expanded ERB mixed code/text extraction so reusable labels are extracted as stable spans while dialogue-like mixed text remains sentence-level.
- Improved translation carryover and same-original propagation, including filtered translation runs that reuse completed translations and propagate results to matching rows outside the current filter.
- Added phased automatic translation ordering (`CSV -> ERH -> ERB`) with phase-boundary glossary reuse for OpenAI-compatible providers.
- Added glossary-hint prompt injection based on overlap between the current batch and translated `CSV`/`ERH` source terms.
- Reworked LM Studio requests to prefer `json_schema` structured output, retry once with stricter settings, and only then fall back to the tokenized pipe protocol.
- Added LM Studio advanced sampling settings and model-aware presets for `Gemma 4` and `Qwen 3.5 9B`.
- Added LM Studio thinking control support for Gemma/Qwen model families, including parser cleanup for leaked reasoning/thought blocks.
- Improved translation settings UX with decimal-friendly numeric inputs, per-tab reset buttons, and a default `false` result-log-file setting.
- Expanded Korean particle correction for regular Korean sentences, split-line particles, reverse parenthesized forms, and additional Josa helper forms.
- Added configurable full-width protected characters in the user dictionary window, while leaving full-width spaces unprotected by default.
- Added completion alerts for extraction and translation runs.
- Improved save-time behavior for Josa rewriting, placeholder preservation, and code-mixed span replacement.

## 0.5 - 2026-05-27

- Added visible app version metadata and `EraTranslator 0.5` window title.
- Improved SQLite progress persistence, row-level progress updates, manual edit propagation, and reference rewriting.
- Added function/expression filtering, Josa rewrite controls, and ERB function correction workflows.
