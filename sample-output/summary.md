# .NET dependency graph report

## Viewing the report

Open `index.html` in a modern browser. The report is self-contained and works offline; keep its generated files together in the same directory.

## Viewer quick guide

- **Search** finds nodes by partial name or ID. Select a node to inspect metadata, detected/effective community assignments, dependency direction, runnable impact, and architectural-role evidence.
- **View** switches between the canonical raw graph, strict filtering, and reachability-preserving contracted filtering. **Collapse local packages** visually combines unambiguous producer projects with their package identities.
- **Community granularity** switches between coarse, standard, and fine detected partitions without changing dependency edges. **Color by** can show effective communities or node kinds.
- **Size by** scales nodes using all transitive dependents or runnable dependents. The remaining filters narrow by neighborhood, kind, edge kind, component, community, TFM, RID, version skew, or minimum runnable-dependent count.
- The **Communities** legend supports navigation and manual rename, recolor, create, reassign, merge, restore, import, and export operations. Manual changes affect presentation only; they do not rewrite dependencies or automatic analysis.
- Select two nodes and use **Explain path** to show a directed dependency path. **Fit all** restores the overview, while the Physics/Layout and Labels panels tune presentation.
- **PNG** exports the current canvas. **Displayed JSON** exports the currently selected and filtered projection; `graph.json` remains the canonical machine-readable graph.

Communities and architectural roles are dependency-topology heuristics, not confirmed business domains or design violations.

## Generated analysis

- Raw graph: 39 nodes, 51 edges
- Display graph: 39 nodes, 51 edges (0 contracted)
- Projects discovered/evaluated/with assets: 4/4/4
- Components: 1; isolated display nodes: 2
- Warnings/errors: 0/0
- Frameworks: net10.0
- RIDs: none
- Communities: Leiden/CPM; standard resolution 0.5; 15 strict-hierarchy groups; trial stability 0.906
- Community projection: project/package weights 3/2/1; tests excluded: True; producer pairs collapsed: True

## Detected communities

These are the automatically detected communities at standard granularity, ordered by size. Names are inferred from member-node labels; use the stable key when correlating a community with `graph.json` or the viewer.

| Community | Stable key | Nodes | Projects | Packages | Runnable | Representative nodes |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| Dot · Graph · Dependency | `community:v1:8cf6393f4dc0efb1` | 7 | 4 | 3 | 1 | DotNetDependencyGraph.IntegrationTests, DotNetDependencyGraph.Core, DotNetDependencyGraph.Core.Tests, NuGet.ProjectModel, DotNetDependencyGraph.Cli |
| Testing | `community:v1:fcb1b7dbd35df319` | 5 | 0 | 5 | 0 | xunit.v3.core.mtp-v1, Microsoft.Testing.Platform, Microsoft.Testing.Extensions.Telemetry, Microsoft.Testing.Extensions.TrxReport.Abstractions, Microsoft.Testing.Platform.MSBuild |
| Xunit | `community:v1:c4ec49890a3800c4` | 4 | 0 | 4 | 0 | xunit.v3.mtp-v1, xunit.v3, xunit.analyzers, xunit.v3.assert |
| Configuration · Data · Protected | `community:v1:469d0d6549e8ce21` | 3 | 0 | 3 | 0 | NuGet.Configuration, NuGet.DependencyResolver.Core, System.Security.Cryptography.ProtectedData |
| Platform | `community:v1:e38345859da5bbd7` | 3 | 0 | 3 | 0 | Newtonsoft.Json, Microsoft.TestPlatform.TestHost, Microsoft.TestPlatform.ObjectModel |
| Async · Bcl · Interfaces | `community:v1:439b0b10e4e6591f` | 2 | 0 | 2 | 0 | xunit.v3.common, Microsoft.Bcl.AsyncInterfaces |
| Code · Coverage · Sdk | `community:v1:f8bbf1abe64226c6` | 2 | 0 | 2 | 0 | Microsoft.NET.Test.Sdk, Microsoft.CodeCoverage |
| Console · Extensibility · Inproc | `community:v1:b1d26b5edcb58291` | 2 | 0 | 2 | 0 | xunit.v3.extensibility.core, xunit.v3.runner.inproc.console |
| Frameworks | `community:v1:2aee617039ce17be` | 2 | 0 | 2 | 0 | NuGet.Common, NuGet.Frameworks |
| Packaging · Protocol · Get | `community:v1:76cad86ec88967c1` | 2 | 0 | 2 | 0 | NuGet.Packaging, NuGet.Protocol |
| Registry · Win32 · Runner | `community:v1:71294a9c6877f097` | 2 | 0 | 2 | 0 | xunit.v3.runner.common, Microsoft.Win32.Registry |
| Versioning · Get · Model | `community:v1:72399749517558d5` | 2 | 0 | 2 | 0 | NuGet.LibraryModel, NuGet.Versioning |
| Application | `community:v1:26df6bee4d9ec894` | 1 | 0 | 1 | 0 | Microsoft.ApplicationInsights |
| Pkcs · Cryptography · Security | `community:v1:63d89639776b868e` | 1 | 0 | 1 | 0 | System.Security.Cryptography.Pkcs |
| Visualstudio | `community:v1:3bf160808884bfc6` | 1 | 0 | 1 | 0 | xunit.runner.visualstudio |

## Components

- Component 0: 39 nodes; representative: xunit.v3.core.mtp-v1, NuGet.Packaging, Microsoft.Testing.Platform
