# Third-Party Notices

MarkdownDotNetRenderer is distributed under the GNU Lesser General Public License v3.0
(`LGPL-3.0-or-later`). It bundles or depends on the third-party components listed below. Their
own copyright and permission notices are reproduced here as required by their licenses.

For the rationale on license compatibility, see
[docs/08-licensing.md](docs/08-licensing.md).

---

## Markdig

- **Used by:** `MarkdownDotNetRenderer.Core` (all phases)
- **Homepage:** https://github.com/xoofx/markdig
- **License:** BSD-2-Clause

```
Copyright (c) 2018-2019, Alexandre Mutel
All rights reserved.

Redistribution and use in source and binary forms, with or without modification
, are permitted provided that the following conditions are met:

   1. Redistributions of source code must retain the above copyright notice, this
      list of conditions and the following disclaimer.

   2. Redistributions in binary form must reproduce the above copyright notice, this
      list of conditions and the following disclaimer in the documentation and/or
      other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY
EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT
SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED
TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR
BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH
DAMAGE.
```

---

## Note on future dependencies

`DocumentFormat.OpenXml` (MIT) is planned for the DOCX writer in
[phase 5](docs/phases/phase-5-docx.md) and is **not** referenced yet. Its notice will be added
here when that dependency is introduced.

Test-only tooling (xUnit, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`,
`coverlet.collector`) is never shipped and therefore not reproduced here.
