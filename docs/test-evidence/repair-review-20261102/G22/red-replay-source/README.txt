G22 red replay source (pre-fix rollback behaviour)

install-office-addin.ps1 / install-visio-addin.ps1 in this directory are the
archived pre-G22 files (copied from ../red-prefix-source/, byte-identical) with
ONLY try-block fault-injection scaffolding added, so the G22 matrix can drive the
same two failures it drives against the fixed installers:

  1. StagedCopyCorrupted - corrupt the staged copy so the production hash
     comparison is what throws (no assertion is skipped).
  2. NewMoveFails        - hold a real handle on a staged file so the rename of
     the stage directory onto the product path genuinely fails.

The recovery catch is NOT modified. Verified after the build by parsing both
files and comparing the top-level catch body against ../red-prefix-source/:

  install-office-addin.ps1 : catch identical to archive = True (6125 bytes)
  install-visio-addin.ps1  : catch identical to archive = True (6121 bytes)

So the only difference between a red and a green run is the rollback logic being
measured; the verifier scripts are byte-identical in both runs.

