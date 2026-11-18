# PuT-Simulationen

<div align="center">

[![Deutsch](https://img.shields.io/badge/%F0%9F%87%A9%F0%9F%87%AA_Deutsch-24292f?style=for-the-badge)](#deutsch)
[![English](https://img.shields.io/badge/%F0%9F%87%AC%F0%9F%87%A7_English-24292f?style=for-the-badge)](#english)

</div>

## Deutsch

Simulationen für die PuT-Praktikumsversuche. Für PuT-3 steht eine virtuelle Steckwerkbank mit NI myDAQ und miniAdap-1 bereit. Du steckst Leitungen und setzt Jumper am Gerätebild. Messart, Kanal, Abtastrate und Ausgabe stellst du in LabVIEW ein.

<div align="center">

[![Start](https://img.shields.io/badge/Start-24292f?style=for-the-badge)](#start)
[![Struktur](https://img.shields.io/badge/Struktur-24292f?style=for-the-badge)](#struktur)
[![Anleitung](https://img.shields.io/badge/Anleitung-24292f?style=for-the-badge)](ANLEITUNG.md#deutsch)
[![Tests](https://img.shields.io/badge/Tests-24292f?style=for-the-badge)](ANLEITUNG.md#tests)

</div>

![Virtuelle Steckwerkbank mit myDAQ, Adapterplatine und Verdrahtungsansicht](PuT-3/Workbench/Assets/vorschau.png)

### Start

PuT-3 unter Windows x64 mit dem .NET 10 SDK aus dem Repository-Ordner bauen.

~~~powershell
dotnet publish .\PuT-3\Workbench\Workbench.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o .\PuT-3\Start
.\PuT-3\Start\PuT3.exe
~~~

In LabVIEW 2026 öffnest du danach **PuT3 Messassistent.vi**. Für eigene Blockdiagramme liegen der Express-Knoten und alle SubVIs im selben Ordner.

### Struktur

Jeder Versuch erhält einen eigenen Ordner. [PuT-3](PuT-3/) enthält die Steckwerkbank. PuT-4 und PuT-5 können später als Ordner auf derselben Ebene hinzukommen.

| Ordner | Inhalt |
| --- | --- |
| [LabVIEW](PuT-3/LabVIEW/) | Messassistent, Funktionsgenerator, SubVIs und beide Bibliotheken des Express-Knotens |
| [Workbench](PuT-3/Workbench/) | C#-/WPF-Anwendung und Schaltungsmodell |
| [Modelle](PuT-3/Modelle/) | Frei drehbares Blender-Modell der Geräte |
| [tools](PuT-3/tools/) | Werkzeuge für VI-Erstellung, LabVIEW-Tests und Gerätegrafik |

## English

Simulations for the PuT lab exercises. PuT-3 provides a virtual wiring bench with the NI myDAQ and miniAdap-1. Wire the connectors and set the jumpers on the bench. Configure measurement type, channel, sample rate and output in LabVIEW.

<div align="center">

[![Quick start](https://img.shields.io/badge/Quick_start-24292f?style=for-the-badge)](#quick-start)
[![Layout](https://img.shields.io/badge/Layout-24292f?style=for-the-badge)](#layout)
[![Guide](https://img.shields.io/badge/Guide-24292f?style=for-the-badge)](ANLEITUNG.md#english)
[![Tests](https://img.shields.io/badge/Tests-24292f?style=for-the-badge)](ANLEITUNG.md#verification)
[![Top](https://img.shields.io/badge/Top-24292f?style=for-the-badge)](#put-simulationen)

</div>

### Quick start

Follow the [build command](#start) from the checkout root on Windows x64 with the .NET 10 SDK. Start `PuT-3\Start\PuT3.exe`, then open **PuT3 Messassistent.vi** in LabVIEW 2026. The same folder contains the Express VI and all the subVIs for your own diagrams.

### Layout

Each exercise has its own directory. [PuT-3](PuT-3/) contains the wiring bench. PuT-4 and PuT-5 can be added alongside it.

| Folder | Contents |
| --- | --- |
| [LabVIEW](PuT-3/LabVIEW/) | Measurement assistant, function generator, subVIs and both Express VI libraries |
| [Workbench](PuT-3/Workbench/) | C#/WPF application and circuit model |
| [Modelle](PuT-3/Modelle/) | Rotatable Blender model of the equipment |
| [tools](PuT-3/tools/) | VI generation, LabVIEW testing and equipment rendering tools |
