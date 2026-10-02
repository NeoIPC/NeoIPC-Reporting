---
paths: "**/*.cs,.editorconfig,.gitattributes,.idea/**,src/NeoIPC.Reporting/scripts/*.R"
---

## C# and .NET

- **Never** use the `\x` escape in character or string literals: it is greedy, consuming one to four hex digits, so `"\x1bescape"` parses as U+01BE followed by `"scape"`, not ESC followed by `"escape"`. Use the fixed-length `\u####` (`"\u001bescape"`), and prefer four-digit Unicode escapes wherever a .NET base-library helper (`Convert.ToChar`, `Encoding`) accepts hex specifiers. *(C#-specific)*

### Writing Text Files

- **.NET**: `File.WriteAllLines` and `StreamWriter.WriteLine` end lines with `Environment.NewLine`, so set `StreamWriter.NewLine`, or join with `"\n"` and call `File.WriteAllText`. `JsonSerializerOptions.WriteIndented` indents with `Environment.NewLine` too unless `NewLine = "\n"` is set, as `Program.cs` sets it for `--emit-schemas`, whose snapshots neoipc-app commits. `Encoding.UTF8` writes a BOM through `GetPreamble()` and a `StreamWriter`, but not through `GetBytes`, and `File.WriteAllText` writes none by default.
- **Editors**: `.editorconfig` sets `charset = utf-8`, never `utf-8-bom`, which stops Visual Studio adding a BOM to a new `.cs` file. Rider's default adds none, so it needs no committed `encodings.xml`, and one appearing in a diff signals a re-introduced deviation.
- **R**: the one R file authored here, `src/NeoIPC.Reporting/scripts/extract-reference-data-metadata.R`, opens a binary connection and passes `sep = "\n"`, since `writeLines(x, path)` writes in text mode, CRLF on Windows, whatever `useBytes` says.
- **The NUL test vector**: `tests/NeoIPC.Reporting.Tests/SecurityRegressionTests.cs` holds a NUL byte as a null-byte-injection test vector, which end-of-line conversion would corrupt, so **never add a `*.cs text` rule**. The hygiene check honours no detected-binary classification, which a stray NUL or CR would turn into a silent exemption from every check, so the file is exempt by its declared `-text` line in `.gitattributes`: a declaration is reviewable where detection is not.
