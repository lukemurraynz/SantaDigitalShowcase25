# Archived Drasi Resources

This directory contains Drasi resource definition files that are not currently used in the automated deployment process but are kept for reference.

## Archived Files

None yet. The *-fixed.yaml files were not found (likely already cleaned up).

## Resource Files in Use

The main \drasi-resources.yaml\ in the parent directory contains all active Source, ContinuousQuery, and Reaction definitions and is the single source of truth for Drasi resources.

## Restoration

To restore an archived file:
```powershell
Move-Item -Path ".\archive\<filename>" -Destination ".\" -Force
```
