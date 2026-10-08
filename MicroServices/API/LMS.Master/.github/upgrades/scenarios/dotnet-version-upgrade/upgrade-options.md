# Upgrade Options — LMS.Master.sln

Assessment: 2 SDK-style class library projects, both low difficulty, no API break findings, package updates required.

## Strategy

### Upgrade Strategy
A small, low-risk scope with two independent libraries is best handled in one coordinated pass.

| Value | Description |
|-------|-------------|
| **All-at-Once** (selected) | Upgrade all scoped projects together, then validate solution build and tests in one pass. |
| Bottom-Up | Upgrade dependency layers first, then move upward with staged validation. |
| Top-Down | Upgrade app entry points first and adapt shared libraries as needed. |

## Project Structure

### Package Management
Multiple projects use direct PackageReference without centralized package management.

| Value | Description |
|-------|-------------|
| **Keep current PackageReference layout** (selected) | Keep package versions in each project file for this upgrade. |
| Introduce Central Package Management | Add Directory.Packages.props and centralize package versions. |

## Modernization

### Nullable Reference Types
Targeting net8.0 allows nullable annotations, but enabling NRT now would expand scope beyond framework/package upgrade.

| Value | Description |
|-------|-------------|
| **Skip for this upgrade** (selected) | Keep nullable settings unchanged and focus on framework/package compatibility. |
| Enable and fix warnings | Turn on nullable and resolve warnings during this upgrade. |
