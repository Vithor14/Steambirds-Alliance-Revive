# Launcher languages

The launcher is written in English, and English is built in (no file needed).
Every other language is one JSON file in this folder, named after its language code:

```
lang\pt-BR.json    Português (Brasil)
lang\es-ES.json    (yours?)
```

The launcher lists every `*.json` of this folder in **Settings → Appearance → Launcher language**.
Files starting with `_` (like `_template.json`) and `*.missing.json` files are not languages.

## Adding a language

1. Copy `_template.json` to `<code>.json` — for example `es-ES.json`, `fr-FR.json`, `de-DE.json`.
2. Fill in `"_language"` with the name of the language as it is written in that language
   (`"Español"`, `"Français"`) and `"_code"` with the code.
3. Translate the **values**. The keys are the English texts of the launcher and must stay exactly as they are.
4. Open the launcher, choose the language in the Settings tab, and check the screens.

Rules for the values:

- Keep the placeholders `{0}`, `{1}`... — the launcher puts names, numbers and paths there.
- Keep `\n` (line break) and `\\` (a single `\` in a path) where the English text has them.
- An empty value, or a key that is missing, shows the English text.
- Keys starting with `_` are notes about the file and are not shown anywhere.
- Save the file as UTF-8.

## Finding what is left

When the launcher closes, it writes `<code>.missing.json` here with the texts it had to show in English
because `<code>.json` has no translation for them (for example after a launcher update added new texts).
Copy those lines into `<code>.json`, translate the values, and delete the `.missing.json` file.

## Server setting descriptions

The last part of `_template.json` (after `"_settings_descriptions"`) has the descriptions shown under the
server settings (Server tab → Progression, Items and loot, Enemies and difficulty, Accounts, Network and multiplayer).
They are optional: without a translation, the launcher shows the comment the server writes above each key of
its `.json` files, which is in English.

## Game language

The language of the **game** is a different setting (Settings → Game → Game language). It lists the
languages the game itself has (`Texts/locales` of the game's configBinary) and passes the chosen one to the game
with `-locale <id>` when it opens. Those texts come from the game files, not from this folder.

## Sharing a translation

Send the `<code>.json` file to the project (pull request on GitHub, or the community channels).
When the launcher is built with `Build launcher.ps1`, every file of `Launcher\lang` is copied to `dist\lang`.

---

**Português:** para traduzir o launcher, copie `_template.json` para `<código>.json`, preencha `"_language"`
e `"_code"` e traduza os valores, sem mudar as chaves (o texto em inglês). Mantenha `{0}`, `{1}`, `\n` e `\\`.
Textos sem tradução aparecem em inglês e são listados em `<código>.missing.json` quando o launcher fecha.
