# PuT-3 Anleitung

<div align="center">

[![Deutsch](https://img.shields.io/badge/%F0%9F%87%A9%F0%9F%87%AA_Deutsch-24292f?style=for-the-badge)](#deutsch)
[![English](https://img.shields.io/badge/%F0%9F%87%AC%F0%9F%87%A7_English-24292f?style=for-the-badge)](#english)

</div>

## Deutsch

<div align="center">

[![Start](https://img.shields.io/badge/Start-24292f?style=for-the-badge)](#start)
[![LabVIEW](https://img.shields.io/badge/LabVIEW-24292f?style=for-the-badge)](#labview)
[![Adressen](https://img.shields.io/badge/Adressen-24292f?style=for-the-badge)](#adressen)
[![Versuch](https://img.shields.io/badge/Versuch-24292f?style=for-the-badge)](#versuch)
[![Tests](https://img.shields.io/badge/Tests-24292f?style=for-the-badge)](#tests)
[![README](https://img.shields.io/badge/README-24292f?style=for-the-badge)](README.md#deutsch)

</div>

### Start

1. `Start\PuT3.exe` öffnen. Wenn die Datei fehlt, den [Build-Befehl](README.md#start) ausführen.
2. In LabVIEW 2026 [PuT3 Messassistent.vi](LabVIEW/PuT3%20Messassistent.vi) öffnen. Messart und physikalischen Kanal wählen und das VI starten. Die Werkbank zeigt den letzten Auftrag an. Für eine gültige Messung den passenden Messkreis stecken.
3. In **Erkunden** einen Anschluss anklicken, um seine Funktion zu sehen. In **Verdrahten** zwei Anschlüsse anklicken, um eine Leitung zu stecken. Über die Leitungsliste lassen sich Verbindungen entfernen und Kabeldefekte einstellen. Die Jumperstellung änderst du direkt am Jumper.
4. In LabVIEW mit dem Express-Knoten oder mit **PuT3 Auftrag.vi** und **PuT3 DAQ.vi** dein eigenes Blockdiagramm aufbauen.

### LabVIEW

| Baustein | Verwendung |
| --- | --- |
| **PuT3 Messassistent.vi** | Messung oder Ausgabe am Frontpanel einrichten und ausprobieren |
| **PuT3 Auftrag.vi** | Typisierten Auftrag für das Blockdiagramm erstellen |
| **PuT3 DAQ.vi** | Auftrag ausführen. **Daten** liefert ein DBL-Array, **Signal** eine Wellenform mit Abtastintervall. Bei Bedarf **Konvertieren in dynamische Daten** für die Express-Analyse anschließen. |
| **PuT3 Funktionsgenerator.vi** | Sinus, Rechteck, Dreieck oder Gleichspannung auf AO ausgeben. Kanal, Frequenz, Spitze-Spitze-Wert und Offset stellst du am VI ein. |
| **PuT3 TCP Austausch.vi** | Datenaustausch mit der Werkbank über `127.0.0.1:48371` |

Für die Funktionspalette den gesamten Ordner **LabVIEW** als **PuT3** unter `user.lib\_express` der LabVIEW-Installation ablegen. Danach LabVIEW neu starten und im Blockdiagramm **Express → Benutzerbibliotheken → PuT3 Virtuelle DAQ** wählen. **PuT3 Virtuelle DAQSource.llb**, **_PuT3 Virtuelle DAQConfig.llb** und die VIs müssen zusammenbleiben. [NI-Anleitung für Express-VIs](https://www.ni.com/docs/en-US/bundle/labview/page/creating-express-vis.html)

### Adressen

| Messart | Kanal | Daten |
| --- | --- | --- |
| AI Spannung | `myDAQ1/ai0`, `myDAQ1/ai1` | Volt je Sample. Kontinuierlich ruft jeder VI-Aufruf einen Block ab. |
| AO Spannung | `myDAQ1/ao0`, `myDAQ1/ao1` | Spannungsverlauf in **Ausgabewerte**. Kontinuierlich wiederholt den Puffer, endliche Ausgabe hält den letzten Wert. |
| DI / DO | `myDAQ1/port0`, `myDAQ1/port0/line0` bis `line7` | Portwert 0–255 oder Einzelbit 0/1. DO verwendet **Ausgabewerte[0]**. |
| CTR Frequenz | `myDAQ1/ctr0` | Hz. PFI-Eingang separat auswählen. |
| DMM Widerstand | `myDAQ1/dmm` | Ohm |
| Ausgänge freigeben | `myDAQ1/port0` | Setzt Ausgänge und Erfassungsfortschritt zurück. Auch direkt in der Werkbank verfügbar. |

Der Analogblock umfasst bis zu 16.384 Werte und bei der Erfassung höchstens 1,5 s. Die virtuelle Abtastrate reicht bis 200 kS/s. **Status** meldet Erfolg (`0`), Begrenzung am Messbereich (`1`) oder einen der folgenden Fehler.

| Code | Ursache |
| --- | --- |
| `-1` | Messart, Kanal oder PFI ungültig |
| `-2` | Parameter oder Ausgabe außerhalb des Bereichs |
| `-3` | Messbezug fehlt oder Widerstandskreis offen |
| `-4` | Elektrischer Konflikt, Überlast oder zu hohe Eingangsspannung |
| `-5` | Kein messbarer Flankenabstand oder undefinierter Digitalpegel |
| `-6` | USB am Modell getrennt |
| `-7` | PFI-Leitung steht auf Digitalausgabe |
| `-8` | Fremdspannung an den DMM-Buchsen bei Widerstandsmessung |
| `-9` | LabVIEW-Verbindungsfehler. Details in **error out**. |

### Versuch

| Teil | An der Werkbank üben |
| --- | --- |
| 5.1 | Schraubklemmen, BNC, DMM-Buchsen und Steckbrücken zuordnen. Funktionsgenerator und Signalgraph als virtuelle SFPs verwenden. |
| 5.2 | Prüfwiderstand anschließen und LabVIEW-Messung ausführen. Mit dem zusätzlichen Testwiderstand die Grenzwertlogik prüfen. |
| 5.3 | Analogausgang als Quelle einstellen und die verdrahtete PFI-Klemme in LabVIEW für die Frequenzmessung wählen. |
| 5.4 | Analoge Blöcke erfassen und mit den geforderten Express-VIs auswerten. |
| 5.5 | Poti messen und in LabVIEW das digitale Portmuster berechnen. Die LEDs folgen dem Port und J7. |

Die Werkbank startet ohne gesteckte Leitungen. Das Modell nähert etwa Massekopplung, Ausgangswiderstände, Stromgrenzen, LED-Lasten und Poti an. Audio-I/O, DMM-Strommessung und USB-Protokoll liegen außerhalb des Versuchsmodells. [Schaltbild miniAdap-1](https://asset.conrad.com/media10/add/160267/c1/-/de/001396697ML01/istruzioni-per-luso-1396697-br-national-instrumentsbr-ni-mydaq-experimentieradapterplatine-1br-br.pdf#page=106) · [NI myDAQ Spezifikation](https://download.ni.com/support/manuals/373061g.pdf)

Der originale NI-DAQ-Assistent kann mit einem in NI MAX simulierten Gerät für Kanalwahl und Aufgabenerstellung verwendet werden. NI-DAQmx erzeugt dabei eigene Signale. Für die Verdrahtungssimulation verwendest du die mitgelieferten PuT-3-VIs. [NI zu simulierten DAQmx-Geräten](https://knowledge.ni.com/KnowledgeArticleDetails?id=kA00Z0000019Nw0SAE&l=en-US)

### Tests

Mit dem gebauten Programm aus dem Repository-Ordner starten.

~~~powershell
$test = Start-Process -FilePath .\Start\PuT3.exe -ArgumentList '--self-test', 'selbsttest.txt' -Wait -PassThru
Get-Content .\selbsttest.txt
$test.ExitCode
~~~

Die C#-Prüfungen umfassen unter anderem Steckverbindungen, Kontaktflächen, Kabeldefekte, Widerstand, Überlast und Fensterlayout. Die nativen LabVIEW-Tests prüfen die Aufträge und den Datenaustausch mit der laufenden Werkbank. Für die Wiederholung LabVIEW 2026 mit lokalem VI Server auf Port `48372` verwenden. `python tools/build_vis.py --cli` und `python tools/build_vis.py --express-run` erzeugen die Prüf-VIs über LabVIEW MCP. Danach startet `python tools/test_labview.py` die Prüfungen. Das Platzieren des Express-Knotens aus der Palette und die manuelle Dialogbedienung sind im automatischen Testumfang offen.

## English

<div align="center">

[![Setup](https://img.shields.io/badge/Setup-24292f?style=for-the-badge)](#setup)
[![LabVIEW](https://img.shields.io/badge/LabVIEW-24292f?style=for-the-badge)](#labview-tools)
[![Channels](https://img.shields.io/badge/Channels-24292f?style=for-the-badge)](#channels)
[![Exercise](https://img.shields.io/badge/Exercise-24292f?style=for-the-badge)](#exercise)
[![Tests](https://img.shields.io/badge/Tests-24292f?style=for-the-badge)](#verification)
[![README](https://img.shields.io/badge/README-24292f?style=for-the-badge)](README.md#english)
[![Top](https://img.shields.io/badge/Top-24292f?style=for-the-badge)](#put-3-anleitung)

</div>

### Setup

1. Launch `Start\PuT3.exe`. If you are starting from a checkout, [build the app first](README.md#start).
2. Open [PuT3 Messassistent.vi](LabVIEW/PuT3%20Messassistent.vi) in LabVIEW 2026. Pick a measurement type and physical channel and run the VI. The bench shows the latest request. Wire the relevant circuit to obtain a reading.
3. Select **Erkunden** (Explore) and click a connector to inspect it. Under **Verdrahten** (Wire), click two connectors to add a lead. Use the lead list to remove a connection or set a cable fault. Click a jumper to change its position.
4. Use **PuT3 Virtuelle DAQ** or the **PuT3 Auftrag.vi** and **PuT3 DAQ.vi** subVIs in your own block diagram.

### LabVIEW tools

| File | Use |
| --- | --- |
| **PuT3 Messassistent.vi** | Configure and try a read or write request on the front panel |
| **PuT3 Auftrag.vi** | Build a typed request |
| **PuT3 DAQ.vi** | Execute the request. **Daten** returns a DBL array. **Signal** returns a waveform with sample spacing. Use **Convert to Dynamic Data** if your Express analysis VI requires it. |
| **PuT3 Funktionsgenerator.vi** | Output a sine, square, triangle or DC signal. Choose channel, frequency, peak-to-peak amplitude and offset. |
| **PuT3 TCP Austausch.vi** | Exchange data with the bench over `127.0.0.1:48371` |

To add the Express VI to the Functions palette, copy the entire **LabVIEW** directory as **PuT3** under `user.lib\_express` in your LabVIEW installation. Restart LabVIEW and choose **Express → User Libraries → PuT3 Virtuelle DAQ**. Keep the source and configuration libraries together with the VIs. [NI guide to creating Express VIs](https://www.ni.com/docs/en-US/bundle/labview/page/creating-express-vis.html)

### Channels

| Measurement | Channel | Data |
| --- | --- | --- |
| AI voltage | `myDAQ1/ai0`, `myDAQ1/ai1` | Volts per sample. Each continuous read gets one block. |
| AO voltage | `myDAQ1/ao0`, `myDAQ1/ao1` | Voltage samples from **Ausgabewerte**. Continuous output repeats the buffer. Finite output holds the last value. |
| DI / DO | `myDAQ1/port0`, `myDAQ1/port0/line0` through `line7` | 0–255 for the port or 0/1 for a bit. DO uses **Ausgabewerte[0]**. |
| CTR frequency | `myDAQ1/ctr0` | Hz. Select the PFI input separately. |
| DMM resistance | `myDAQ1/dmm` | Ohms |
| Release outputs | `myDAQ1/port0` | Reset outputs and acquisition progress. Also available in the bench. |

Analog blocks contain up to 16,384 values and up to 1.5 s of acquired data at a virtual sample rate of up to 200 kS/s. **Status** returns `0` on success, `1` on input range clipping, and negative values for errors.

| Code | Cause |
| --- | --- |
| `-1` | Invalid operation, channel or PFI input |
| `-2` | Parameter or output outside the supported range |
| `-3` | Missing measurement reference or open resistance circuit |
| `-4` | Electrical conflict, overload or excessive input voltage |
| `-5` | No measurable edge interval or undefined digital level |
| `-6` | Simulated USB cable disconnected |
| `-7` | PFI line configured as a digital output |
| `-8` | External voltage at the DMM terminals during resistance measurement |
| `-9` | LabVIEW connection error. See **error out**. |

### Exercise

| Part | Practice on the bench |
| --- | --- |
| 5.1 | Locate terminals, BNC connectors, DMM jacks and jumpers. Use the function generator and waveform graph as virtual soft front panels. |
| 5.2 | Wire a test resistor and measure it in LabVIEW. Use the extra resistor to check your threshold logic. |
| 5.3 | Set up analog output as a signal source. Measure frequency using the PFI terminal you wired. |
| 5.4 | Read analog blocks and process them using the required Express VIs. |
| 5.5 | Read the potentiometer in LabVIEW and compute the digital port pattern. The port and J7 drive the LEDs. |

The bench starts with the leads disconnected. The circuit model approximates ground coupling, output resistance, current limits, LED loads and the potentiometer. Audio I/O, DMM current measurement and USB protocol are outside the exercise model. [miniAdap-1 schematic](https://asset.conrad.com/media10/add/160267/c1/-/de/001396697ML01/istruzioni-per-luso-1396697-br-national-instrumentsbr-ni-mydaq-experimentieradapterplatine-1br-br.pdf#page=106) · [NI myDAQ specifications](https://download.ni.com/support/manuals/373061g.pdf)

The original NI DAQ Assistant supports channel selection and task setup with an NI MAX simulated device. NI-DAQmx generates its own readings in that mode. Use the included PuT-3 VIs to get readings from the wiring bench. [NI on simulated DAQmx devices](https://knowledge.ni.com/KnowledgeArticleDetails?id=kA00Z0000019Nw0SAE&l=en-US)

### Verification

Run the [PowerShell self-test command](#tests) in a built checkout. The C# tests cover wiring, connector hit areas, cable faults, resistance, overload and window layout. The native LabVIEW tests exercise requests and data exchange against the running bench. With LabVIEW 2026 and its local VI server on port `48372`, run `python tools/build_vis.py --cli` and `python tools/build_vis.py --express-run` to generate the test VIs through LabVIEW MCP, then run `python tools/test_labview.py`. Palette insertion and manual interaction with the Express configuration dialog remain outside the automated tests.
