# Writing standard: ASD-STE100

All text that a person or an agent reads must obey ASD-STE100 (Simplified Technical English), Issue 9.

## Scope

This rule applies to these text bodies:

- reports, root-cause analyses, and gate records
- pull-request titles and bodies
- issues, requests, and comments
- changelogs and release notes
- commit message bodies
- documentation, including `AGENTS.md`, `CLAUDE.md`, and the published docs
- requirement text in `traceable-reqs.toml`
- messages between agents

Code, identifiers, commands, paths, log text, and quoted text keep their exact form. Quoted text counts as one word.

## Rules

The source of the rules is the distilled list in [BigscreenVR/ASD-proof-reading](https://github.com/BigscreenVR/ASD-proof-reading/blob/main/spec/ASD-STE100_distilled.md). That list gives one line for each rule of the standard. Read it before you write a long text.

These rules cause most of the errors:

- Write a maximum of 20 words in each sentence of a procedure.
- Write a maximum of 25 words in each sentence of descriptive text.
- Write only one instruction in each sentence.
- Write each instruction in the imperative form.
- When the reader must know a condition first, put the condition at the start of the sentence.
- Use the active voice.
- Do not use the semicolon.
- Do not use contractions. Do not omit articles or verbs to make a sentence shorter.
- Do not make complex verb constructions such as "has been fixed" or "can be adjusted".
- Do not use a phrasal verb. Write "remove", not "take out".
- Use one technical noun for one item. Do not change the name of an item in the text.
- Write a maximum of six sentences in each paragraph. Give each paragraph only one topic.
- Use a vertical list when a sentence must include many items.

## Words

Use the approved word on the right, not the word on the left:

- ensure, verify: make sure
- should, shall: must
- perform, carry out, implement: do
- may: can
- need, required: necessary
- however: but
- therefore: thus
- since (because): because
- via: through
- whether: if
- determine: find
- create: make
- additional: more
- avoid: prevent

Technical nouns and technical verbs of this project are permitted. Examples are the names of the components, commands, and data of the project.

## Audit

ASD-jev audits Markdown files and `traceable-reqs.toml`. It is in [BigscreenVR/ASD-proof-reading](https://github.com/BigscreenVR/ASD-proof-reading). The README of that repository gives the installation steps. The audit needs an `OPENROUTER_API_KEY`.

Run the audit on a changed instruction file or a long document before you finish the change:

```sh
uv run --project <asd-jev> <asd-jev>/probes/asd_audit.py <file> --out <report.json>
```

Read the findings and correct the text. The audit does not change files.
