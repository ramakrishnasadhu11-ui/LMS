# .NET Version Upgrade

## Strategy
**Selected**: All-at-Once
**Rationale**: Two SDK-style class libraries with low migration difficulty and no API break findings can be upgraded together safely.

### Execution Constraints
- Update all scoped projects to net8.0 in the same execution phase.
- Apply package updates required by the assessment before final validation.
- Run full solution build and resolve all warnings before task completion.
- Run available tests after build stabilization.

## Preferences
- **Flow Mode**: Automatic
- **Target Framework**: net8.0
- **Commit Strategy**: After Each Task

## Upgrade Options
**Source**: .github/upgrades/scenarios/dotnet-version-upgrade/upgrade-options.md

### Strategy
- Upgrade Strategy: All-at-Once

### Project Structure
- Package Management: Keep current PackageReference layout

### Modernization
- Nullable Reference Types: Skip for this upgrade

## Source Control
- **Source Branch**: feature/mycode
- **Working Branch**: upgrade-dotnet-8
- **Commit Strategy**: After Each Task
- **Branch Sync**: Auto (Merge)
