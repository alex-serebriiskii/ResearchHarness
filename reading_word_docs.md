# Reading Word Documents

## Capability

`pandoc` 3.9 is installed and available on PATH in every bash session. It was installed via `winget` (JohnMacFarlane.Pandoc) and its executable is copied to `C:\Users\vboxuser\.bun\bin\pandoc.exe`, which is on the harness PATH.

## How to Read a .docx File

Run pandoc from bash, converting to plain text:

```
pandoc path/to/file.docx -t plain
```

Or to markdown (preserves headings, lists, tables):

```
pandoc path/to/file.docx -t markdown
```

The output is printed to stdout and can be read directly or piped into further processing.

## What Does Not Work

- The `read` tool does not support `.docx` files. It handles PNG, JPG, and PDF only.
- The `fetch` tool supports `.docx` at a remote URL, but not local paths.
- `pandoc` is the correct tool for local Word documents.

## Verification

```
pandoc --version
# pandoc 3.9
```

## Notes

- The canonical install location is `C:\Users\vboxuser\AppData\Local\Pandoc\pandoc.exe`.
- The copy in `.bun\bin` exists solely to keep `pandoc` on the harness PATH without requiring a process restart. If pandoc is upgraded, the copy at `C:\Users\vboxuser\.bun\bin\pandoc.exe` should be refreshed.
- Python and python-docx are not installed on this machine; pandoc is the only available path for local `.docx` extraction.
