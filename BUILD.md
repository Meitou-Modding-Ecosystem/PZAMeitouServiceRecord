# PZA's Service Record for Meitou: build

Author: PZA. Licence: GNU GPL version 3 or later (`LICENSE`).

This is the source of PZA's Service Record for Meitou 0.1.7 (beta, commit `897e44c` of the development repository). Each release also carries a `-source.zip` with everything the DLL is built from, the shared code included.

You need:
- the .NET SDK (10 or newer)
- an unpacked Meitou release (0.20.1 or newer, win-x64)
- [PZA's Meitou UI](https://github.com/Meitou-Modding-Ecosystem/PZAMeitouUi) beside this folder (`..\PZAMeitouUi\src`; its code is compiled in), also in the source zip
- the RE_Kenshi plugin's data mod and language files (`PZAServiceRecord\dist\`), also in the source zip

Build (PowerShell):

```
.\mod\build.ps1 -MeitouDir <the unpacked Meitou folder>
```

It stages the install folder in `mod\out\`. (`-Check` runs the developer's offline checker, which is not part of this repository.)
