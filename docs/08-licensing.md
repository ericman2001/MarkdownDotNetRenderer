# 08 — Licensing

## Decision

**MarkdownDotNetRenderer is licensed under the GNU Lesser General Public License v3.0
(LGPLv3).** The repository previously carried plain GPLv3.

## Rationale

The primary artifact is a **library** meant to be referenced by other applications, including
closed-source and commercial ones (a build tool inside a proprietary product, an internal
report generator, a commercial docs pipeline). Plain GPLv3 would require any such consumer to
release its own source under a GPL-compatible license, which would effectively bar the
library's main use case.

LGPLv3 keeps the copyleft where it matters and drops it where it would be counterproductive:

| Concern | Under LGPLv3 |
| --- | --- |
| Using the library from a closed-source app (NuGet reference / dynamic link) | Allowed, no obligation to open the app's source |
| Modifying the library itself | Modifications to the library must be released under LGPLv3 (or GPLv3) |
| Static linking / single-file / AOT-published bundling | Allowed, provided the consumer conveys the LGPL notice and enables relinking with a modified version — LGPLv3 §4 (supply object code or source of the app, or use a suitable shared mechanism) |
| Redistributing the library, modified or not | Notices and license text must be preserved; recipients get the same rights |
| Patents / warranty | Inherited from GPLv3, which LGPLv3 incorporates by reference |

The alternative of MIT/Apache-2.0 was rejected: improvements to the *renderer itself* should
flow back, and LGPLv3 achieves that without taxing consumers. AGPL was rejected as
inappropriate for a non-networked library.

Note for consumers doing AOT/single-file publishing: that is *static* linking under LGPLv3 §4,
so the "allow the user to relink with a modified library" condition applies. The practical
route is to note in your product's third-party notices that MarkdownDotNetRenderer is LGPLv3,
link to this repository, and be prepared to supply either your object files or a build
mechanism that lets a recipient rebuild with a modified copy of the library. This is called out
here because AOT publishing is a first-class scenario for this project
([06-aot-and-dependencies](06-aot-and-dependencies.md)).

## What changes in the repository

LGPLv3 is not a standalone license text: it is a **set of additional permissions layered on top
of GPLv3**. The customary FSF distribution is therefore *both* texts:

| File | Contents |
| --- | --- |
| `LICENSE` | The full **GPLv3** text (the base license; FSF names this file `COPYING`) |
| `LICENSE.LESSER` | The full **LGPLv3** text — the additional permissions (FSF names this `COPYING.LESSER`) |

The `LICENSE` / `LICENSE.LESSER` naming is used here rather than `COPYING` / `COPYING.LESSER`
because it is what GitHub, NuGet tooling, and license scanners recognise; the contents are the
unmodified FSF texts either way.

Additional items to carry out as part of implementation (phase 0 onward):

- Every source file gets the short LGPL header notice: project name, copyright line, and the
  "This library is free software; you can redistribute it and/or modify it under the terms of
  the GNU Lesser General Public License as published by the Free Software Foundation, either
  version 3 of the License, or (at your option) any later version… without even the implied
  warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE" paragraphs, plus a pointer
  to `LICENSE.LESSER`.
- Project files set `<PackageLicenseExpression>LGPL-3.0-or-later</PackageLicenseExpression>`
  (SPDX identifier) so NuGet and downstream scanners report the right license.
- `README.md` states the LGPLv3 license and links to both files.
- The `-or-later` form ("either version 3 … or any later version") is used, matching FSF
  guidance.

## Dependency license compatibility

| Dependency | License | Compatible with distributing this project under LGPLv3? |
| --- | --- | --- |
| Markdig | BSD-2-Clause | **Yes.** Permissive; imposes only attribution. No conflict with LGPLv3 terms. |
| DocumentFormat.OpenXml | MIT | **Yes.** Permissive; attribution only. |
| .NET runtime / BCL | MIT | **Yes.** |
| xUnit and test tooling | Apache-2.0 / MIT | **Yes**, and irrelevant to distribution — test-only, never shipped. |

Both runtime dependencies are permissive, so they can be combined with LGPLv3 code freely and
the combined work distributes under LGPLv3 while their own notices are preserved. Practical
obligations for us:

- Retain the Markdig and OpenXml copyright/permission notices in any binary distribution
  (a `THIRD-PARTY-NOTICES.md` file, added in phase 0).
- Keep the dependency budget in
  [06-aot-and-dependencies](06-aot-and-dependencies.md) enforced: **no GPL or AGPL
  dependency may be added**, since that would force the whole work to GPL/AGPL and destroy the
  closed-source-consumer property that motivated LGPLv3. Any candidate rasterizer for
  [phase 6](phases/phase-6-docx-png-fallback.md) must be license-checked on this basis before
  its technical merits are even considered.

## Non-code assets

Documentation under `docs/` and the README are covered by the same LGPLv3 grant for
simplicity. Sample Markdown fixtures used in tests are original work by the project authors.
Mermaid's own syntax is a language, not copyrightable subject matter, and no Mermaid source
code is copied — the renderers here are independent implementations
([04-mermaid-engine](04-mermaid-engine.md)), which is also a licensing reason to avoid
consulting `mermaid.js` internals while implementing layout.
