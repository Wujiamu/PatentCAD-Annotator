G18 red replay source (pre-fix catch bodies)

These four files are the production installers/uninstallers of the G18 snapshot
with ONLY their top-level recovery catch replaced by the R-029 shape:
  registry.remove -> registry.restore -> files.restore
and, for the installers, the pre-fix "skip the old directory restore because
the install path still exists" branch.

Everything above each catch (parameter block, staging, recovery-copy creation,
normal success path) is the fixed G18 source unchanged, so the only difference
between red and green is the rollback order being measured.

Rebuild procedure used to produce these files:
  1. copy office-com-addin/<file> to a scratch directory
  2. locate the first top-level catch with the regex
     (?m)^catch\s*\{(?=\r?\n\s+\$failure)
  3. truncate the file at that match and append the pre-fix catch body
  4. also copy dist/ next to the scripts and run the same verify-*.ps1

The two verify-*.ps1 scripts are byte-identical to the fixed ones: the red run
uses the same assertions, so a PASS in red would mean the assertions cannot
detect the defect.
