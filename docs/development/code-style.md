# Code Style

## Frontend

### Prettier

Configured in `.prettierrc`:

| Setting | Value |
|---|---|
| Print width | 140 characters |
| Quotes | Single quotes |
| Indentation | 2 spaces |

### ESLint

Configured with:

- Angular plugin
- Perfectionist plugin (sorted class members)
- Prettier plugin (formatting integration)

### EditorConfig

Configured in `ClientApp/.editorconfig`:

- 2-space indentation
- UTF-8 encoding

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
