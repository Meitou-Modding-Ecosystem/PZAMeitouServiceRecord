# PZA's Service Record for Meitou (beta 0.1.1)

Every fighter keeps a service record: kills, knockouts, one-hit knockouts, limbs taken and lost, damage dealt and taken, biggest hits, and a
radar chart of their fighting style. After a big fight you get an **After-Action Report** with advice on what to keep and what to improve.
A port of PZA's Service Record for RE_Kenshi, aiming at the same numbers and rules.

**Download:** the mod zip and its source zip are on the [Releases page](https://github.com/PZA-Mods/PZAMeitouServiceRecord/releases). Building from source: [BUILD.md](BUILD.md).

## Hotkeys

| Key | What it does |
|---|---|
| **Shift+R** | Opens or closes the Service Record window |

Plain R stays Meitou's. Nothing fires while you type in a text box.

## The window and the records
- **Shift+R** window, tabs **Fighter** (your selected character's record and radar chart; Back / Next step through your fighters),
  **Squad** (everyone with a record, six ways to sort), **Recent** (the latest kills and knockouts with times), **Reports** (the After-Action
  Reports, in colour) and **?** (help).
- Three short lines under the attributes on the character screen (kills / KOs / limbs, damage dealt / taken, KO'd / limbs lost); hover one
  for the full text.
- **After-Action Reports** are written after a fight of 8+ hits once 30 s pass without a hit, and kept as text files in
  `%LOCALAPPDATA%\kenshi\PZAServiceRecord\reports\`.
- Records are saved with your game. A save made with the RE_Kenshi version should bring its records along (not tried yet).
- The **-** button shrinks the window to a tiny button: drag it, mouse wheel for its opacity, click for the window. Its place is kept.

## Requires
**Meitou 0.20.1 up to 0.21.x.** It refuses to load on other Meitou versions.

## Install
- Unpack `PZAServiceRecord.Meitou-0.1.1-beta.zip` into Kenshi's folder (`Kenshi\mods\PZAServiceRecord\`), then tick
  **PZAServiceRecord** in Meitou's mod list. If you also use PZA's Service Record for RE_Kenshi, it is the same folder on purpose: each game
  loads only its own file.
- No items or world data; load order doesn't matter. You can remove it at any time; whether the records come back after saving without it has not been tried yet.

## This is a beta
**The rules are checked offline against the RE_Kenshi version's own code (thousands of random fights through both, every number equal), on
Meitou 0.20.1 and 0.21.0. This build has not been played in game yet.**

Known limits:
- The Military Manager (Shift+M, the chain of command) of the RE_Kenshi version is not in this build yet.
- Texts in English, Russian, Chinese and Japanese (machine translations, not yet checked by native speakers).

## Reporting a bug
What happened (for a wrong count: who hit whom, how, and what the record says), how to make it happen again, your Meitou version and mod
list, and Meitou's `log.txt` (in your Meitou folder) copied right after that session. Our lines start with `PZA's Service Record:`. Post it
in its thread on the Meitou Discord or in the Workshop page's Bug reports discussion.

## Licence and source
GNU GPL version 3 or later; the source is attached to the release (`-source.zip`). Made by **PZA**. Unofficial fan-made mod; not affiliated
with Lo-Fi Games.
