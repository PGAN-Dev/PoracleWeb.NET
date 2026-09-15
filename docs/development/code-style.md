# Code Style

## Frontend

### Prettier

Configured in `.prettierrc`:

| Setting | Value |
|---|---|
| Print width | 140 characters |
| Quotes | Single quotes |
| Indentation | 2 spaces |
| Arrow parens | Avoided — `x => x.id`, not `(x) => x.id` |
| Bracket same line | On — a multi-line tag's `>` stays on the last attribute line |

`prettier-check` covers `src/**/*.{ts,html,scss}`. HTML files are parsed with the `angular` parser.

### ESLint

Configured in `.eslintrc.json` with `@angular-eslint`, `@typescript-eslint`, `perfectionist`
(sorted classes, objects, interfaces, enums, exports and array includes), `sort-class-members`,
`unused-imports`, and `plugin:prettier/recommended` so formatting failures surface as lint errors.
Component and directive selectors use the `app` prefix.

### EditorConfig

`ClientApp/.editorconfig` sets 2-space indentation, UTF-8, a trailing newline, and single quotes
for `.ts`.

### Commands

```bash
cd Applications/Pgan.PoracleWebNet.App/ClientApp

# Check lint
npm run lint

# Check formatting
npm run prettier-check

# Auto-fix lint issues
npx eslint --fix src/

# Auto-format code
npm run prettier-format
```

## Backend

The repository root `.editorconfig` carries the C# rules. The two you will notice while writing code:

- 4-space indentation (the frontend's 2 is a `ClientApp/` override).
- `this.` is required on fields, properties, methods and events — `dotnet_style_qualification_for_*`
  is set to `warning`, and `dotnet_analyzer_diagnostic.severity = warning` applies to every other
  .NET style rule in the file.

## Line endings on Windows

There is no `.gitattributes`, so nothing normalises line endings for you. With `core.autocrlf=true`
Git hands you CRLF working files, Prettier defaults to LF, and `npm run prettier-check` fails on
every file before you have changed anything. Set `git config core.autocrlf false` and re-check out
`ClientApp/` before touching the frontend.
