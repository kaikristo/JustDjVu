# JustDjVu

JustDjVu is a desktop DjVu reader for Windows built with C# and WPF. It opens
`.djvu` and `.djv` files and provides multiple viewing modes, customizable
shortcuts, bookmarks, text-layer search, and automatic reading-position
restoration.

## Table of contents

- [Quick start](#quick-start)
- [System requirements](#system-requirements)
- [Features](#features)
- [Opening a document](#opening-a-document)
- [Controls and keyboard shortcuts](#controls-and-keyboard-shortcuts)
- [Settings and languages](#settings-and-languages)
- [Building from source](#building-from-source)
- [Creating a self-contained build](#creating-a-self-contained-build)
- [Troubleshooting](#troubleshooting)
- [Licenses](#licenses)

## Quick start

The ready-to-use self-contained build does not require a separate .NET or
DjVuLibre installation.

1. Download `dist/JustDjVu-win-x64.zip` from the repository or the corresponding
   GitHub Release, then extract the **entire** archive to a dedicated folder.
2. Run `JustDjVu.exe`.
3. Open a document through **File → Open**, drag a file into the application
   window, or use Windows **Open with**.

If the repository is already available on your computer, the ready-to-use
executable is located at:

```text
dist\JustDjVu-win-x64\JustDjVu.exe
```

Important: do not move `JustDjVu.exe` away from the other published files. The
`Tools\DjVuLibre` directory must remain next to the application for the decoder
to work.

## System requirements

### Ready-to-use self-contained build

- 64-bit Windows 10 or Windows 11;
- the fully extracted application directory with its original file structure;
- no .NET Runtime installation is required.

### Building from source

- Windows 10 or Windows 11;
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0);
- PowerShell 5.1 or later;
- Git, only if you need to clone the source repository.

The required DjVuLibre components are included in the project and do not need
to be installed separately.

## Features

- Open `.djvu` and `.djv` files from the menu, by drag and drop, from the
  command line, or through Windows **Open with**.
- Asynchronous rendering, a bounded page cache, and adjacent-page prefetching
  for faster navigation.
- **Single page**, **Continuous**, and **Facing pages** viewing modes.
- Zoom from 10% to 400%, actual size, fit page, and fit width.
- Page rotation and full-screen mode.
- Thumbnails, page-number navigation, and per-document bookmarks.
- Mouse text selection directly on rendered pages, with clipboard copying.
- Search within an embedded DjVu text layer.
- Copy the current page, export it to PNG, save a document copy, and print the
  current page.
- Light and dark themes.
- Two customizable shortcuts for every action.
- Page navigation with the mouse wheel.
- A recent-files list.
- Automatic restoration of the last document and reading page.
- Russian, English, German, French, and Spanish interface languages.

> Text selection and search are available only when the document contains a
> text/OCR layer. JustDjVu does not perform optical character recognition
> itself.

## Opening a document

### From JustDjVu

- Select **File → Open** or press `Ctrl+O`.
- Alternatively, drag a `.djvu` or `.djv` file from File Explorer into the
  JustDjVu window.

### Using Windows “Open with”

To select the application manually, right-click a document, choose
**Open with → Choose another app**, and browse to `JustDjVu.exe`.

To register JustDjVu automatically:

1. Run `JustDjVu.exe`.
2. Select **Tools → Register for .djvu**.

Registration applies only to the current Windows user and does not require
administrator privileges. JustDjVu will then appear in the application list for
both `.djvu` and `.djv` files.

### From the command line

```powershell
.\JustDjVu.exe "C:\Books\document.djvu"
```

If the file path contains spaces, enclose it in quotation marks.

## Controls and keyboard shortcuts

Default shortcuts:

| Action | Primary | Secondary |
|---|---:|---:|
| Open document | `Ctrl+O` | — |
| Close document | `Ctrl+W` | — |
| Print current page | `Ctrl+P` | — |
| Copy selected text | `Ctrl+C` | — |
| Search text | `Ctrl+F` | — |
| Previous page | `←` | Wheel up |
| Next page | `→` | Wheel down |
| First page | `Home` | — |
| Last page | `End` | — |
| Zoom in | `Ctrl++` | — |
| Zoom out | `Ctrl+-` | — |
| Actual size (100%) | `Ctrl+0` | — |
| Fit page | `F` | — |
| Fit width | `W` | — |
| Rotate page | `Ctrl+R` | — |
| Show or hide the sidebar | `F4` | — |
| Full-screen mode | `F11` | — |
| Add or remove a bookmark | `Ctrl+B` | — |

In Single page and Facing pages modes, the mouse wheel changes pages. In
Continuous mode, it scrolls through the document and can advance to the next
page after reaching an edge. Hold `Ctrl` while using the wheel to change the
zoom level.

To customize the controls:

1. Open **Tools → Settings → Keyboard shortcuts**.
2. Select a field in the **Primary** or **Secondary** column.
3. Press the desired key combination or move the mouse wheel.
4. Select **Save**.

Use **Reset all** to restore the default shortcuts.

### Selecting and copying text

When a DjVu page contains a hidden text layer:

1. Drag the mouse over words on the rendered page.
2. Press `Ctrl+C` to copy the selection.

You can also right-click a page and choose **Copy selected text**. While the
text layer is focused, `Ctrl+A` selects all text on that page, `Shift` extends
an existing selection, and `Esc` clears it. Selection follows the page when it
is zoomed or rotated and works in Single page, Continuous, and Facing pages
modes.

## Settings and languages

The **Tools → Settings** window allows you to configure:

- the interface language;
- the light or dark theme;
- the default viewing mode;
- the default page sizing mode;
- a custom zoom level;
- sidebar and toolbar visibility;
- whether the last document should open automatically;
- primary and secondary shortcuts.

Available interface languages:

- Russian;
- English;
- German;
- French;
- Spanish.

The selected language is applied as soon as the settings are saved. Restarting
the application is not required.

Settings, recent files, bookmarks, the last document, and reading positions are
stored separately for the current Windows user:

```text
%LOCALAPPDATA%\JustDjVu\settings.json
```

By default, JustDjVu reopens the last available document at its saved reading
page on the next launch. This behavior can be disabled on the **General**
settings tab. If the file was moved or deleted, the application starts without
opening it.

## Building from source

Open PowerShell in the repository root and verify that the required SDK is
available:

```powershell
dotnet --version
```

Restore and build the solution:

```powershell
dotnet restore .\JustDjvu.slnx
dotnet build .\JustDjvu.slnx -c Release
```

Build output:

```text
JustDjvu\bin\Release\net10.0-windows\JustDjVu.exe
```

Run the project without a separate build command:

```powershell
dotnet run --project .\JustDjvu\JustDjvu.csproj -c Release
```

Run the project and immediately open a document:

```powershell
dotnet run --project .\JustDjvu\JustDjvu.csproj -c Release -- "C:\Books\document.djvu"
```

A regular build requires the .NET 10 Desktop Runtime on the destination
computer. Create a self-contained build to distribute the application to a
computer without .NET.

## Creating a self-contained build

Run the following command from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

The script creates a self-contained 64-bit build and its ZIP archive:

```text
dist\JustDjVu-win-x64\
dist\JustDjVu-win-x64.zip
```

The resulting directory can be copied to another Windows 10 or Windows 11
computer without installing .NET. Distribute the entire directory, not only the
executable.

To create a framework-dependent publication that requires the .NET 10 Desktop
Runtime:

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1 -FrameworkDependent
```

## Project structure

```text
JustDjvu.slnx                 .NET solution
JustDjvu\                     WPF application source code
JustDjvu\Tools\DjVuLibre\     DjVuLibre decoder and libraries
publish.ps1                   distribution build script
dist\                         ready-to-use self-contained builds
LICENSE                       JustDjVu license
THIRD-PARTY-NOTICES.md        third-party component notices
```

## Troubleshooting

### The application does not start after extraction

- Make sure the entire archive was extracted and that the application is not
  being run directly from an archive manager.
- Confirm that `Tools\DjVuLibre\ddjvu.exe` and the accompanying libraries are
  present.
- If you are using a regular rather than self-contained build, install the
  [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

### Windows SmartScreen displays a warning

The current build is not signed with a commercial code-signing certificate, so
Windows may display a warning for a downloaded executable. Run the application
only if you trust its source or have built it yourself.

### A document opens, but its pages are not displayed

Check whether antivirus software removed files from `Tools\DjVuLibre`, then
extract the entire archive again. Moving `JustDjVu.exe` without this directory
prevents the decoder from working.

### Search returns no results

The document may not contain a text layer. Page viewing and image export remain
available, but searchable text must first be added with an external OCR tool.

### The last document is not restored

- Enable **Open the last document on startup** in Settings.
- Make sure the document was not moved or deleted.
- Confirm that `%LOCALAPPDATA%\JustDjVu\settings.json` is writable.

### JustDjVu does not appear under “Open with”

Run the application and select **Tools → Register for .djvu** again. Then reopen
the **Open with** menu in File Explorer.

## Licenses

JustDjVu source code is distributed under the [MIT License](LICENSE).

Decoding is performed by separate
[DjVuLibre 3.5.29](https://djvu.sourceforge.net/) utilities licensed under GNU
GPL version 2 or later. The complete license text is available at
`JustDjvu\Tools\DjVuLibre\COPYING.txt`. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for additional details.
