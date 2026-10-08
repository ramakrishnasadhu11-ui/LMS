# .NET Version Upgrade Plan

## Overview

**Target**: Upgrade LMS.Master solution projects from .NET Core 2.2 to .NET 8
**Scope**: Small scope, 2 SDK-style class library projects with package refresh and validation

## Tasks

### 01-update-target-frameworks: Update target frameworks to net8.0

Update all scoped project files so they consistently target net8.0. This task is focused on framework alignment only and keeps changes minimal to avoid mixing compatibility fixes with version targeting. The assessment indicates both impacted projects are SDK-style and low complexity, so this can be done in a single pass.

**Done when**: Every scoped project targets net8.0 and the solution restores successfully.

---

### 02-upgrade-packages: Upgrade recommended and vulnerable NuGet packages

Apply package updates identified in the assessment, prioritizing vulnerable and deprecated dependencies first, then recommended updates. Keep package changes constrained to versions compatible with net8.0 and avoid optional modernization changes that are out of scope for this run.

**Done when**: Vulnerable/deprecated package findings from the assessment are addressed and package restore is clean.

---

### 03-validate-build-and-tests: Validate build, warnings, and tests

Run full solution validation after framework and package updates. Resolve build breaks and warnings in modified projects, then run available tests to confirm behavior remains stable after upgrade.

**Done when**: Solution build passes without errors, warnings in modified projects are resolved, and tests pass (or documented if unavailable).
