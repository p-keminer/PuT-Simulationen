# PuT-Simulationen

<div align="center">

[![Deutsch](https://img.shields.io/badge/%F0%9F%87%A9%F0%9F%87%AA_Deutsch-24292f?style=for-the-badge)](#deutsch)
[![English](https://img.shields.io/badge/%F0%9F%87%AC%F0%9F%87%A7_English-24292f?style=for-the-badge)](#english)

</div>

## Deutsch

Simulationen für die PuT-Praktikumsversuche. Für PuT-3 steht eine virtuelle Steckwerkbank mit NI myDAQ und miniAdap-1 bereit. Du steckst Leitungen und setzt Jumper am Gerätebild. Messart, Kanal, Abtastrate und Ausgabe stellst du in LabVIEW ein.

<div align="center">

[![Start](https://img.shields.io/badge/Start-24292f?style=for-the-badge)](#start)
[![Voraussetzungen](https://img.shields.io/badge/Voraussetzungen-24292f?style=for-the-badge)](#voraussetzungen)
[![Struktur](https://img.shields.io/badge/Struktur-24292f?style=for-the-badge)](#struktur)
[![Anleitung](https://img.shields.io/badge/Anleitung-24292f?style=for-the-badge)](ANLEITUNG.md#deutsch)
[![Tests](https://img.shields.io/badge/Tests-24292f?style=for-the-badge)](ANLEITUNG.md#tests)
[![Lizenz](https://img.shields.io/badge/Lizenz-MIT-24292f?style=for-the-badge)](#lizenz)

</div>

![Virtuelle Steckwerkbank mit myDAQ, Adapterplatine und Verdrahtungsansicht](PuT-3/Workbench/Assets/vorschau.png)

### Voraussetzungen

| Zweck | Benötigt |
| --- | --- |
| Steckwerkbank aus dem Klon bauen und testen | Windows x64, PowerShell und [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Die veröffentlichte EXE enthält die .NET-Laufzeit. |
| Versuch in LabVIEW durchführen | LabVIEW 2026 mit aktiver Lizenz und die mitgelieferten VIs. Steckwerkbank und LabVIEW laufen auf demselben Rechner. Für die Simulation brauchst du weder ein physisches myDAQ noch NI-DAQmx, ELVISmx oder LabVIEW MCP. |
| Native LabVIEW-Tests | Zusätzlich Python 3 (getestet mit 3.12), die [NI LabVIEW Command Line Interface](https://www.ni.com/en/support/downloads/software-products/download.ni-labview-command-line-interface.html) als `LabVIEWCLI` im `PATH` und einen lokalen LabVIEW-VI-Server auf TCP-Port `48372`. Der Test startet die gebaute `PuT-3\Start\PuT3.exe` am freien Port `48371`. |
| Prüf-VIs erzeugen oder VIs weiterentwickeln | [LabVIEW MCP v2.3.2](https://github.com/Zuehlke/labview-mcp/releases/tag/v2.3.2) mit dem mitgelieferten `pylabview`-Backend. Setze `LABVIEW_MCP_EXE` auf deine lokale `LabVIEWMCP.exe`. Das Python-Skript startet den Server selbst. |
| 3D-Modell öffnen oder Gerätegrafik rendern | Optional Blender 5.2. |

Die Befehle für die Steckwerkbank-Tests, die Erzeugung der Prüf-VIs und den LabVIEW-Testlauf stehen unter [Tests in der Anleitung](ANLEITUNG.md#tests).

### Start

PuT-3 unter Windows x64 mit dem .NET 10 SDK aus dem Repository-Ordner bauen.

~~~powershell
dotnet publish .\PuT-3\Workbench\Workbench.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o .\PuT-3\Start
.\PuT-3\Start\PuT3.exe
~~~

In `PuT-3\LabVIEW` liegen **Virtual DAQ Signal erzeugen.vi** und **Virtual DAQ Signal erfassen.vi**. Für dein eigenes Blockdiagramm findest du im Unterordner **Bausteine** die SubVIs zum Einstellen und Ausführen eines Messauftrags.

Nach dem Klonen oder Aktualisieren bindest du **Virtual DAQ** mit dem [Installationsskript](PuT-3/Install-LabVIEW.ps1) in LabVIEW ein. Führe im Repository-Ordner aus und öffne anschließend LabVIEW.

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\PuT-3\Install-LabVIEW.ps1
~~~

### Struktur

Jeder Versuch erhält einen eigenen Ordner. [PuT-3](PuT-3/) enthält die Steckwerkbank. PuT-4 und PuT-5 können später als Ordner auf derselben Ebene hinzukommen.

| Ordner | Inhalt |
| --- | --- |
| [LabVIEW](PuT-3/LabVIEW/) | Messassistent, Funktionsgenerator, SubVIs und beide Bibliotheken des Express-Knotens |
| [Workbench](PuT-3/Workbench/) | C#-/WPF-Anwendung und Schaltungsmodell |
| [Modelle](PuT-3/Modelle/) | Frei drehbares Blender-Modell der Geräte |
| [tools](PuT-3/tools/) | Werkzeuge für VI-Erstellung, LabVIEW-Tests und Gerätegrafik |

### Lizenz

Die projekteeigenen Inhalte stehen unter der [MIT-Lizenz](LICENSE). Für NI LabVIEW und die externen Werkzeuge gelten deren jeweilige Lizenzbedingungen.

## English

Simulations for the PuT lab exercises. PuT-3 provides a virtual wiring bench with the NI myDAQ and miniAdap-1. Wire the connectors and set the jumpers on the bench. Configure measurement type, channel, sample rate and output in LabVIEW.

<div align="center">

[![Quick start](https://img.shields.io/badge/Quick_start-24292f?style=for-the-badge)](#quick-start)
[![Requirements](https://img.shields.io/badge/Requirements-24292f?style=for-the-badge)](#requirements)
[![Layout](https://img.shields.io/badge/Layout-24292f?style=for-the-badge)](#layout)
[![Guide](https://img.shields.io/badge/Guide-24292f?style=for-the-badge)](ANLEITUNG.md#english)
[![Tests](https://img.shields.io/badge/Tests-24292f?style=for-the-badge)](ANLEITUNG.md#verification)
[![License](https://img.shields.io/badge/License-MIT-24292f?style=for-the-badge)](#license)
[![Top](https://img.shields.io/badge/Top-24292f?style=for-the-badge)](#put-simulationen)

</div>

### Requirements

| Purpose | Requirements |
| --- | --- |
| Build the wiring bench from a clone and run its self-tests | Windows x64, PowerShell, and the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). The published EXE bundles the .NET runtime. |
| Work through the exercise in LabVIEW | Licensed LabVIEW 2026 and the included VIs. Run LabVIEW and the wiring bench on the same machine. The simulation needs no physical myDAQ, NI-DAQmx, ELVISmx, or LabVIEW MCP. |
| Run native LabVIEW tests | Also install Python 3 (tested with 3.12) and the [NI LabVIEW Command Line Interface](https://www.ni.com/en/support/downloads/software-products/download.ni-labview-command-line-interface.html), with `LabVIEWCLI` on your `PATH`. Enable the local LabVIEW VI Server on TCP port `48372`. The test starts the built `PuT-3\Start\PuT3.exe` and uses port `48371`, which must be free. |
| Generate test VIs or develop VIs | [LabVIEW MCP v2.3.2](https://github.com/Zuehlke/labview-mcp/releases/tag/v2.3.2) with the bundled `pylabview` backend. Set `LABVIEW_MCP_EXE` to your local `LabVIEWMCP.exe`. The Python script starts the server. |
| Open the 3D model or render the equipment artwork | Optional Blender 5.2. |

See [Verification in the guide](ANLEITUNG.md#verification) for the bench self-test, test VI generation and LabVIEW test commands.

### Quick start

Follow the [build command](#start) from the checkout root on Windows x64 with the .NET 10 SDK. Start `PuT-3\Start\PuT3.exe`. The `PuT-3\LabVIEW` directory contains **Virtual DAQ Signal erzeugen.vi** and **Virtual DAQ Signal erfassen.vi**. The **Bausteine** subfolder contains the subVIs for configuring and executing requests from your own block diagram.

After cloning or updating, run the [LabVIEW palette installer](#start) from the checkout root and open LabVIEW.

### Layout

Each exercise has its own directory. [PuT-3](PuT-3/) contains the wiring bench. PuT-4 and PuT-5 can be added alongside it.

| Folder | Contents |
| --- | --- |
| [LabVIEW](PuT-3/LabVIEW/) | Measurement assistant, function generator, subVIs and both Express VI libraries |
| [Workbench](PuT-3/Workbench/) | C#/WPF application and circuit model |
| [Modelle](PuT-3/Modelle/) | Rotatable Blender model of the equipment |
| [tools](PuT-3/tools/) | VI generation, LabVIEW testing and equipment rendering tools |

### License

The project's own content is available under the [MIT License](LICENSE). NI LabVIEW and third-party tools are subject to their respective licenses.
