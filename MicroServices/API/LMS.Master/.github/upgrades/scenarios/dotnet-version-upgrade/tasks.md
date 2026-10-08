# .NET Version Upgrade Progress

## Overview

This workflow upgrades the LMS.Master solution projects from .NET Core 2.2 to .NET 8. The approach is all-at-once for the scoped projects, followed by package remediation and full validation.

**Progress**: 0/3 tasks complete <progress value="0" max="100"></progress> 0%

## Tasks

- 🔲 01-update-target-frameworks: Update target frameworks to net8.0
- 🔲 02-upgrade-packages: Upgrade recommended and vulnerable NuGet packages
- 🔲 03-validate-build-and-tests: Validate build, warnings, and tests
