\# ADR-001: Language and stack



\- Status: Accepted

\- Date: 2026-09-29



\## Context



We are building a reusable library for RDW open data, exposed through a

CLI, a REST API and possibly a web interface. The developer is learning

software development with AI assistance and wants professional tooling.

The realistic options were C#/.NET and Python.



\## Decision



We use C# on .NET 10 (LTS, supported until November 2028), developed in

Visual Studio 2026 Community.



\## Reasons



\- Strong typing catches many mistakes at compile time, which helps a learner.

\- Visual Studio offers excellent debugging and tooling.

\- The library, CLI and REST API can share one codebase in one solution.

\- Self-contained single-file publish makes the CLI easy to distribute.



\## Consequences



\- Contributors need the .NET SDK installed to build the project.

\- Python-specific data tooling is not available out of the box.

\- We follow the .NET release cycle and must plan an upgrade before LTS ends.

