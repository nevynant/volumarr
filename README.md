# Volumarr

![License](https://img.shields.io/badge/license-GPLv3-blue)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Fork of Sonarr](https://img.shields.io/badge/fork%20of-Sonarr-00ccff)
![Status](https://img.shields.io/badge/status-personal%20project-orange)
![Code](https://img.shields.io/badge/code-AI--assisted-8A2BE2)

Volumarr is an audiobook series tracker. It monitors your Audible-catalog
series, watches indexers for new books, and grabs, sorts, and renames them
as they're released — the same kind of automation Sonarr provides for TV,
applied to audiobooks instead.

## About this project

Volumarr is a modified version of [Sonarr](https://github.com/Sonarr/Sonarr),
adapted to track audiobook series via Audible instead of TV series via
TheTVDB. It is **not affiliated with or endorsed by the Sonarr/Servarr
project** — it's an independent fork.

- Modified from Sonarr, starting 2026.
- Metadata source swapped from TheTVDB to Audible's catalog API.
- Episode/season concepts repurposed to represent books within a series.
- Currently a personal/homelab project, not an official release.

## AI disclosure

The Volumarr modifications in this fork were written substantially by an
AI assistant (Anthropic's Claude, via Claude Code), working under human
direction and review. Changes were developed iteratively against a real
deployment and its unit test suite, but this is AI-assisted code in a
personal project — read it with the same scrutiny you'd give any
unfamiliar codebase, and see the license's no-warranty terms below. The
underlying Sonarr codebase is the Sonarr team's human-authored work.

## License

Volumarr is licensed under the **GNU General Public License v3.0**, the
same license as the Sonarr codebase it's built on. See [LICENSE.md](LICENSE.md)
for the full text. As with the original license, this means:

- The source code for Volumarr, including all modifications, is available
  to anyone who receives a copy of the software.
- You're free to run, study, modify, and share Volumarr, provided any
  copies or derivative works you distribute remain under the same license.
- Volumarr comes with no warranty; see the license for details.

Original Sonarr copyright 2010-2025, the Sonarr Team. Modifications for
Volumarr copyright 2026, the Volumarr contributors.

## Getting Started

Volumarr is currently intended for personal/homelab deployment via Docker.
There is no public download page, installer, or hosted documentation yet.

### Finding an indexer

Volumarr searches indexers the same way Sonarr does (directly, or via
Prowlarr/Jackett), but most public/private trackers are TV- or movie-focused
and won't have audiobook content at all. **AudioBookBay** is a known
audiobook-focused tracker; if you're running Jackett, check its indexer list
for "AudioBookBay" and add it there, then let Prowlarr proxy it (or add it
directly to Volumarr's own indexer settings) the same way you would any
other indexer. Without at least one audiobook-focused indexer configured,
searches will run without errors but never find anything.

The default quality profile ("Any") is already configured to accept
audiobook releases out of the box — no manual adjustment needed.

## Acknowledgements

Volumarr exists because of the work of the Sonarr team and the broader
Servarr project. The core application, download/indexer automation, and
UI framework this project modifies are theirs; audiobook-specific behavior
(Audible metadata, book-based file organization, indexer search adjustments)
is the work added in this fork.
