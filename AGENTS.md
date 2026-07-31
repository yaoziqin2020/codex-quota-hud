# Codex Quota HUD Agent Rules

## Release boundary

- Public Setup packages are for ordinary users. They must not expose, create,
  or default to Developer Preview entry points.
- Public Setup defaults to current-user startup and creation of the normal
  `Codex Quota HUD` desktop shortcut. The normal shortcut launches without
  `--preview`.
- Developer Preview remains available only from source/ZIP with the explicit
  `--preview` argument, or through a separately maintained shortcut on the
  maintainer's own machine.
- A maintainer-machine shortcut or other local development convenience is
  local state. Never infer or copy it into public Setup behavior.

## Contradiction guard

- Preserve previously approved product and release contracts. If a later
  request appears to reverse one, explicitly identify the conflict and ask for
  confirmation before changing code, packaging, tests, documentation, tags,
  releases, or assets.
- In particular, distinguish statements about the maintainer's current
  machine from requirements for ordinary users. Ambiguous words such as
  "installed", "desktop shortcut", or "final state" do not authorize changing
  the public installer contract.
- Installer acceptance must verify user-facing intent, not merely consistency
  between code and tests.
