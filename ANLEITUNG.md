# PuT-Simulationen Anleitung

<div align="center">

[![Deutsch](https://img.shields.io/badge/%F0%9F%87%A9%F0%9F%87%AA_Deutsch-24292f?style=for-the-badge)](#deutsch)
[![English](https://img.shields.io/badge/%F0%9F%87%AC%F0%9F%87%A7_English-24292f?style=for-the-badge)](#english)

</div>

## Deutsch

<div align="center">

[![Start](https://img.shields.io/badge/Start-24292f?style=for-the-badge)](#start)
[![LabVIEW](https://img.shields.io/badge/LabVIEW-24292f?style=for-the-badge)](#labview)
[![Adressen](https://img.shields.io/badge/Adressen-24292f?style=for-the-badge)](#adressen)
[![Grenzwerte](https://img.shields.io/badge/Grenzwerte-24292f?style=for-the-badge)](#hardwaregrenzen)
[![Versuch](https://img.shields.io/badge/Versuch-24292f?style=for-the-badge)](#versuch)
[![Tests](https://img.shields.io/badge/Tests-24292f?style=for-the-badge)](#tests)
[![README](https://img.shields.io/badge/README-24292f?style=for-the-badge)](README.md#deutsch)

</div>

### PuT-3

#### Start

1. `PuT-3\Start\PuT3.exe` öffnen. Wenn die Datei fehlt, den [Build-Befehl](README.md#start) ausführen.
2. In LabVIEW 2026 [Virtual DAQ Signal erfassen.vi](PuT-3/LabVIEW/Virtual%20DAQ%20Signal%20erfassen.vi) öffnen. Messart und physikalischen Kanal wählen und das VI starten. Die Werkbank zeigt den letzten Auftrag an. Für eine gültige Messung den passenden Messkreis stecken.
3. In **Erkunden** einen Anschluss anklicken, um seine Funktion zu sehen. In **Verdrahten** zwei Anschlüsse anklicken, um eine Leitung zu stecken. Klicke auf ein Kabel im Bild oder wähle es unter **Leitungen**. **Leitung entfernen** oder **Entf** löscht die Auswahl. Den Kabeldefekt stellst du bei der ausgewählten Leitung ein. Die Jumperstellung änderst du direkt am Jumper.
4. Für dein eigenes Blockdiagramm **Virtual DAQ** aus der Express-Palette verwenden. Die SubVIs aus `PuT-3\LabVIEW\Bausteine` stehen für direkte Aufrufe zur Verfügung.

#### LabVIEW

| Baustein | Verwendung |
| --- | --- |
| **Virtual DAQ** (Express-VI) | **1. Messart → 2. Anschluss → 3. Einstellungen**. Der Dialog blendet unbenutzte Parameter aus, sperrt unpassende Kanäle und zeigt, welcher Datenanschluss zur Aufgabe passt. |
| **Virtual DAQ Signal erzeugen.vi** | Sinus, Rechteck, Dreieck oder Gleichspannung auf AO ausgeben. Die Werkbank spielt das Signal nach dem VI-Lauf weiter ab. **AO0 stoppen** oder **AO1 stoppen** beendet den gewählten Ausgang. |
| **Virtual DAQ Signal erfassen.vi** | Direkt am Frontpanel messen oder über **Ausgabewerte** eigene Werte an AO oder DO schicken |
| **Bausteine → Virtual DAQ Messauftrag einstellen.vi** | Messart, Kanal und Einstellungen für dein Blockdiagramm als **Auftrag** bündeln |
| **Bausteine → Virtual DAQ Daten lesen und schreiben.vi** | Auftrag an die Werkbank senden und das Ergebnis empfangen. **Daten** liefert ein DBL-Array, **Signal** eine Wellenform mit Abtastintervall. Für AO und DO die Werte an **Ausgabewerte** anschließen. |
| **Intern** | Gemeinsame Ausführung, TCP-Verbindung und die beiden Express-Bibliotheken |

Für Versuchsteil 2 **Virtual DAQ** in dein eigenes Blockdiagramm setzen. Im Dialog **DMM Widerstand** und `myDAQ1/dmm` wählen. Bei `Status = 0` enthält **Messwert** den Widerstand in Ohm. In der Werkbank den spannungsfreien Prüfwiderstand zwischen **VΩ+** und **COM** anschließen. Der Prüfstrom gehört zum DMM-Messauftrag und wird in der Schaltung idealisiert berechnet. Die Werkbank zeigt den Prüfpfad und den Widerstand. Eine konkrete Stromstärke oder Prüfspannung zeigt sie nicht an, weil die myDAQ-Spezifikation dafür keine Werte nennt. [NI myDAQ Device Specifications, Tabelle 7](https://download.ni.com/support/manuals/373061g.pdf#page=9) · [NI zu Ohmmessungen mit dem myDAQ](https://download.ni.com/pub/gdc/tut/lab_1_dc_measurements.pdf)

Zum Prüfen einer digitalen Ausgabe **Virtual DAQ Signal erfassen.vi** öffnen und **DO Schreiben**, `myDAQ1/port0` und **Ausgabewerte[0]** = `5` einstellen. Das VI einmal ausführen. Bei geschlossenem J7 und freien DIO-Anschlüssen leuchten D0 und D2. Die kleine Zahl links am Array ist sein Index.

Für die Express-Palette den [Installationsbefehl aus der README](README.md#start) ausführen. Das Skript erkennt eine Standardinstallation von LabVIEW 2026 und kann mit `-LabVIEWRoot` auf einen anderen Installationsordner gesetzt werden. Es sichert ersetzte Dateien unter `%LOCALAPPDATA%\PuT-Simulationen\LabVIEW-Sicherungen`. Falls Windows den Schreibzugriff verweigert, den Befehl in einer als Administrator gestarteten PowerShell wiederholen. Nach dem Neustart im Blockdiagramm rechtsklicken und gegebenenfalls unten den Doppelpfeil anklicken. Unter **Eigene Bibliotheken → Eigene Bibliotheken (Express) → PuT3 → paletteMenu** das blaue **Virtual DAQ** auswählen. [NI-Anleitung für Express-VIs](https://www.ni.com/docs/en-US/bundle/labview/page/creating-express-vis.html)

#### Adressen

| Messart | Kanal | Daten |
| --- | --- | --- |
| AI Spannung | `myDAQ1/ai0`, `myDAQ1/ai1` | Volt je Sample. Kontinuierlich ruft jeder VI-Aufruf einen Block ab. |
| AO Spannung | `myDAQ1/ao0`, `myDAQ1/ao1` | Spannungsverlauf in **Ausgabewerte**. Kontinuierlich wiederholt den Puffer, endliche Ausgabe hält den letzten Wert. |
| DI / DO | `myDAQ1/port0`, `myDAQ1/port0/line0` bis `line7` | Portwert 0–255 oder Einzelbit 0/1. Für DO genau einen Wert als Array anschließen. Das Schreiben einer Einzelleitung ändert nur dieses Bit. |
| CTR Frequenz | `myDAQ1/ctr0`, Signal an PFI1 / DIO1 (GATE) | Hz |
| DMM Widerstand | `myDAQ1/dmm` | Ohm |
| Ausgänge zurücksetzen | Gesamtes myDAQ | Stoppt beide AO-Signale, trennt die virtuellen AO-Treiber und schaltet alle DIO-Leitungen auf Eingang. Die Werkbank-Schaltfläche **Alle Ausgänge stoppen** hat dieselbe Wirkung. |

Der Funktionsgenerator berechnet die Ausgaberate aus Frequenz × Punkte pro Periode. Ein wiederholter Aufruf mit denselben Ausgangsdaten setzt die Signalphase nicht zurück. Eine kontinuierliche AI-Messung liest aufeinanderfolgende Blöcke auf demselben Abtastraster. Die Ausgabe läuft zwischen den VI-Aufrufen weiter. Für **5.3** benutzt die Ein-Zähler-Frequenzmessung PFI1 (GATE). PFI0 ist SOURCE, PFI2 AUX und PFI3 OUT. [NI zur myDAQ-Kanalbelegung](https://www.ni.com/docs/en-US/bundle/ni-daqmx/page/mydaqphyschanns.html)

Der Analogblock umfasst bis zu 16.384 Werte und bei der Erfassung höchstens 1,5 s. Die virtuelle Abtastrate reicht bis 200 kS/s. **Status** meldet Erfolg (`0`), Begrenzung am Messbereich (`1`) oder einen der folgenden Fehler.

| Code | Ursache |
| --- | --- |
| `-1` | Messart, Kanal oder PFI ungültig |
| `-2` | Parameter oder Ausgabe außerhalb des Bereichs |
| `-3` | Messbezug fehlt oder Widerstandskreis offen bzw. oberhalb des Messbereichs |
| `-4` | Schutz ausgelöst oder AI außerhalb des Arbeitsbereichs |
| `-5` | Kein messbarer Flankenabstand oder undefinierter Digitalpegel |
| `-6` | USB am Modell getrennt |
| `-7` | PFI-Leitung steht auf Digitalausgabe |
| `-8` | Fremdspannung an den DMM-Buchsen bei Widerstandsmessung |
| `-9` | LabVIEW-Verbindungsfehler. Details in **error out**. |

#### Hardwaregrenzen

| Anschluss | Im Modell geprüfte Grenzen |
| --- | --- |
| DIO0–7 und PFI | Eingang 0 bis 5 V gegen DGND. Low bis 0,8 V, High ab 2 V. Der Bereich dazwischen ergibt bei DI einen undefinierten Pegel. Als Ausgang liefert der Pin nominal 3,3 V für High und höchstens 4 mA je Leitung. |
| AI0/AI1 | Messbereiche ±2 V und ±10 V. Jede Plus- und Minusklemme darf im Betrieb bis ±10,5 V gegen AGND führen. Außerhalb des eingestellten Messbereichs begrenzt der Simulator die Werte. Außerhalb des Arbeitsbereichs meldet er einen Fehler. |
| AO0/AO1 | ±2 V oder ±10 V und maximal 2 mA je Kanal. Zwei aktive Ausgänge brauchen denselben Spannungsbereich. Für AI und AO liegt die Schutzangabe bei ±16 V gegen AGND. |
| DMM Widerstand | Automatische Bereichswahl zwischen 200 Ω und 20 MΩ. Auflösung gemäß Tabelle 7, zum Beispiel 1 Ω im 2-kΩ-Bereich. |
| Versorgung | ±15-V-Schienen bis 32 mA je Schiene, +5 V bis 100 mA. Die Simulation nutzt das typische gemeinsame Budget von 500 mW für Versorgung, AO und DO. NI spezifiziert als Mindestwert 100 mW. |

Bei Überstrom, DIO-Spannung außerhalb des vorgesehenen Bereichs oder Überschreitung der analogen Schutzangabe schaltet das Modell die virtuelle Versorgung und die Ausgänge ab. Die Meldung nennt den betroffenen Anschluss, sofern der Fehler einem Pin zugeordnet werden kann. Löse die falsche Verbindung oder korrigiere das Signal und wähle **Schutz zurücksetzen**. AO bleiben danach aus, DIO stehen auf Eingang. LabVIEW kann anschließend neue Ausgabewerte senden. Ein bleibender Versorgungskurzschluss löst sofort erneut aus.

J1 startet geschlossen und verbindet AGND mit DGND. Bei offenem J1 und fehlender externer Masseverbindung meldet eine Messung von AO an DIO oder PFI fehlenden Bezug (`-3`). J7 startet geöffnet. Bei geschlossenem J7 belasten die Adapter-LEDs das Signal über je 680 Ω laut Schaltplan. Die angenommene LED-Durchlassspannung beträgt 1,8 V. Wenn die LED-Last zum AO-Überstrom beiträgt, nennt die Schutzmeldung J7. Öffne dann J7 und setze den Schutz zurück. [miniAdap-1, Schaltplan S. 106 und Jumperbelegung S. 108–111](https://asset.conrad.com/media10/add/160267/c1/-/de/001396697ML01/istruzioni-per-luso-1396697-br-national-instrumentsbr-ni-mydaq-experimentieradapterplatine-1br-br.pdf#page=108)

Die Werkbank verwendet die spezifizierten 16 Bit für AI und AO und 1 Ω Ausgangsimpedanz für AO. Sie berechnet Spannungen mit idealisierter Wandlung und ohne künstliches Messrauschen. Rail-Innenwiderstände von 1 Ω, 40 Ω für den Digitalausgang und die Fremdspannungserkennung der virtuellen Ohmmessung ab mehr als 50 mV sind Modellannahmen. Die Werkbank prüft bei einem einzelnen veränderlichen Analogausgang zusätzlich die Signalspitzen. Bei zwei veränderlichen Ausgängen prüft sie die berechneten Abtastzeitpunkte. Schutzabschaltung und Abschalten eines Analogausgangs sind Modellaktionen. Die tatsächliche Beschädigung von Bauteilen und das Verhalten des physischen myDAQ beim Stoppen eines Auftrags bleiben offen. [NI myDAQ Device Specifications, S. 1 und 3–6](https://download.ni.com/support/manuals/373061g.pdf) · [NI zum Analogausgang nach Task-Stopp](https://knowledge.ni.com/KnowledgeArticleDetails?id=kA00Z000000g05ZSAQ&l=en-US)

#### Versuch

| Teil | An der Werkbank üben |
| --- | --- |
| 5.1 | Schraubklemmen, BNC, DMM-Buchsen und Steckbrücken zuordnen. Funktionsgenerator und Signalgraph als virtuelle SFPs verwenden. |
| 5.2 | Prüfwiderstand anschließen und LabVIEW-Messung ausführen. Mit dem zusätzlichen Testwiderstand die Grenzwertlogik prüfen. |
| 5.3 | Analogausgang als Quelle einstellen, das Signal an PFI1 / DIO1 führen und in LabVIEW über `myDAQ1/ctr0` erfassen. |
| 5.4 | Analoge Blöcke erfassen und mit den geforderten Express-VIs auswerten. |
| 5.5 | Poti messen und in LabVIEW das digitale Portmuster berechnen. J7 für die LED-Anzeige schließen. |

Die Werkbank startet ohne gesteckte Leitungen. Audio-I/O, DMM-Strommessung und USB-Protokoll liegen außerhalb des Versuchsmodells.

Der originale NI-DAQ-Assistent kann mit einem in NI MAX simulierten Gerät für Kanalwahl und Aufgabenerstellung verwendet werden. NI-DAQmx erzeugt dabei eigene Signale. Für die Verdrahtungssimulation verwendest du die mitgelieferten PuT-3-VIs. [NI zu simulierten DAQmx-Geräten](https://knowledge.ni.com/KnowledgeArticleDetails?id=kA00Z0000019Nw0SAE&l=en-US)

#### Tests

Im Repository-Ordner beginnen und für die Prüfungen in den Ordner `PuT-3` wechseln.

~~~powershell
Set-Location .\PuT-3
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
[![Limits](https://img.shields.io/badge/Limits-24292f?style=for-the-badge)](#hardware-limits)
[![Exercise](https://img.shields.io/badge/Exercise-24292f?style=for-the-badge)](#exercise)
[![Tests](https://img.shields.io/badge/Tests-24292f?style=for-the-badge)](#verification)
[![README](https://img.shields.io/badge/README-24292f?style=for-the-badge)](README.md#english)
[![Top](https://img.shields.io/badge/Top-24292f?style=for-the-badge)](#put-simulationen-anleitung)

</div>

### PuT-3 lab bench

#### Setup

1. Launch `PuT-3\Start\PuT3.exe`. If you are starting from a checkout, [build the app first](README.md#start).
2. Open [Virtual DAQ Signal erfassen.vi](PuT-3/LabVIEW/Virtual%20DAQ%20Signal%20erfassen.vi) in LabVIEW 2026. Pick a measurement type and physical channel and run the VI. The bench shows the latest request. Wire the relevant circuit to obtain a reading.
3. Select **Erkunden** (Explore) and click a connector to inspect it. Under **Verdrahten** (Wire), click two connectors to add a lead. Click a lead on the bench or select it in the lead list. **Leitung entfernen** (Remove lead) or **Delete** removes the selected lead. Set a cable fault in the selected lead’s options. Click a jumper to change its position.
4. Use **Virtual DAQ** from the Express palette in your own block diagram. The subVIs in `PuT-3\LabVIEW\Bausteine` are available for direct calls.

#### LabVIEW tools

| File | Use |
| --- | --- |
| **Virtual DAQ** (Express VI) | Choose the operation, then the terminal, then its settings. The dialog hides unused parameters, disables incompatible channels and shows which data terminal to use. |
| **Virtual DAQ Signal erzeugen.vi** | Output a sine, square, triangle or DC signal. The bench keeps generating the waveform after the VI returns. Use **AO0 stoppen** or **AO1 stoppen** to stop that output. |
| **Virtual DAQ Signal erfassen.vi** | Take measurements or send your own AO or DO values through **Ausgabewerte** on the front panel |
| **Bausteine → Virtual DAQ Messauftrag einstellen.vi** | Bundle the operation, channel and settings into a request for your block diagram |
| **Bausteine → Virtual DAQ Daten lesen und schreiben.vi** | Send the request and receive the response. **Daten** returns a DBL array. **Signal** returns a waveform with sample spacing. Wire AO and DO values to **Ausgabewerte**. |
| **Intern** | Shared execution, TCP transport and the two Express libraries |

For Part 2, place **Virtual DAQ** in your own block diagram. Select **DMM Widerstand** and `myDAQ1/dmm` in the dialog. On success (`Status = 0`), **Messwert** contains the resistance in ohms. On the bench, connect the unpowered test resistor between **VΩ+** and **COM**. The DMM request models the excitation current internally. The bench shows the measurement path and resistance. The myDAQ specifications leave the excitation current and voltage unspecified. [NI myDAQ Device Specifications, Table 7](https://download.ni.com/support/manuals/373061g.pdf#page=9) · [NI myDAQ resistance lab](https://download.ni.com/pub/gdc/tut/lab_1_dc_measurements.pdf)

To try a digital output, open **Virtual DAQ Signal erfassen.vi** and set **DO Schreiben**, `myDAQ1/port0` and **Ausgabewerte[0]** = `5`, then run it once. With J7 installed and the DIO terminals free, D0 and D2 light up. The small number on the left is the array index.

Run the [palette installer from the README](README.md#start). It finds the standard LabVIEW 2026 installation. Use `-LabVIEWRoot` for a custom installation directory. It backs up replaced files under `%LOCALAPPDATA%\PuT-Simulationen\LabVIEW-Sicherungen`. If Windows denies write access, run the command from an elevated PowerShell. After restarting, right-click the block diagram and expand the palette with the double arrow if needed. Choose the blue **Virtual DAQ** icon under **User Libraries → Express User Libraries → PuT3 → paletteMenu**. [NI guide to creating Express VIs](https://www.ni.com/docs/en-US/bundle/labview/page/creating-express-vis.html)

#### Channels

| Measurement | Channel | Data |
| --- | --- | --- |
| AI voltage | `myDAQ1/ai0`, `myDAQ1/ai1` | Volts per sample. Each continuous read gets one block. |
| AO voltage | `myDAQ1/ao0`, `myDAQ1/ao1` | Voltage samples from **Ausgabewerte**. Continuous output repeats the buffer. Finite output holds the last value. |
| DI / DO | `myDAQ1/port0`, `myDAQ1/port0/line0` through `line7` | 0–255 for the port or 0/1 for a bit. Connect a one-element array for DO. Writing a single line affects only that bit. |
| CTR frequency | `myDAQ1/ctr0`, signal to PFI1 / DIO1 (GATE) | Hz |
| DMM resistance | `myDAQ1/dmm` | Ohms |
| Reset outputs | Whole myDAQ | Stops both AO signals, disconnects their simulated drivers and switches all DIO lines to input mode. The equivalent bench button is **Alle Ausgänge stoppen** (Stop all outputs). |

The function generator computes its output sample rate as frequency × points per period. Repeating the same output request preserves the signal phase. Continuous AI captures consecutive blocks on the same sample grid. The output continues between VI calls. In **5.3**, the one-counter frequency measurement uses PFI1 (GATE). PFI0 is SOURCE, PFI2 is AUX and PFI3 is OUT. [NI myDAQ channel assignments](https://www.ni.com/docs/en-US/bundle/ni-daqmx/page/mydaqphyschanns.html)

Analog blocks contain up to 16,384 values and up to 1.5 s of acquired data at a virtual sample rate of up to 200 kS/s. **Status** returns `0` on success, `1` on input range clipping, and negative values for errors.

| Code | Cause |
| --- | --- |
| `-1` | Invalid operation, channel or PFI input |
| `-2` | Parameter or output outside the supported range |
| `-3` | Missing measurement reference or open or overrange resistance circuit |
| `-4` | Protection tripped or AI outside the working range |
| `-5` | No measurable edge interval or undefined digital level |
| `-6` | Simulated USB cable disconnected |
| `-7` | PFI line configured as a digital output |
| `-8` | External voltage at the DMM terminals during resistance measurement |
| `-9` | LabVIEW connection error. See **error out**. |

#### Hardware limits

| Connection | Limits checked by the model |
| --- | --- |
| DIO0–7 and PFI | 0 to 5 V to DGND as input. Low up to 0.8 V and high from 2 V. DI reports an undefined level between these thresholds. Nominal output high is 3.3 V with up to 4 mA per line. |
| AI0/AI1 | ±2 V and ±10 V measurement ranges. Each positive and negative terminal has a working range of ±10.5 V to AGND. The simulator clips values outside the selected measurement range and reports an error outside the working range. |
| AO0/AO1 | ±2 V or ±10 V and up to 2 mA per channel. Both active outputs need the same voltage range. The protection rating for AI and AO is ±16 V to AGND. |
| DMM resistance | Auto-ranging between 200 Ω and 20 MΩ, with the resolution in Table 7. For example, 1 Ω in the 2 kΩ range. |
| Power | Up to 32 mA per ±15 V rail and 100 mA on +5 V. The simulator uses the typical shared 500 mW output budget for rails, AO and DO. NI specifies a 100 mW minimum. |

Overcurrent, DIO voltage outside the intended range and voltage beyond the analog protection rating shut down the virtual supply and outputs. The message identifies the affected terminal when applicable. Remove the incorrect connection or correct the source and select **Schutz zurücksetzen** (Reset protection). AO stays off and DIO returns to input mode until LabVIEW sends new output values. A persistent short on a supply rail immediately trips protection again.

J1 starts closed and connects AGND to DGND. With J1 open and no external ground connection, reading AO through DIO or PFI reports a missing reference (`-3`). J7 starts open. With J7 installed, the adapter LEDs load the signal through 680 Ω per LED as shown in the schematic. The modeled LED forward voltage is 1.8 V. The protection message names J7 when the LED load contributes to AO overcurrent. Open J7 and reset protection. [miniAdap-1, schematic p. 106 and jumper descriptions pp. 108–111](https://asset.conrad.com/media10/add/160267/c1/-/de/001396697ML01/istruzioni-per-luso-1396697-br-national-instrumentsbr-ni-mydaq-experimentieradapterplatine-1br-br.pdf#page=108)

The bench uses the specified 16-bit resolution for AI and AO and 1 Ω output impedance for AO. It computes voltages with ideal conversion and without artificial measurement noise. The 1 Ω rail resistance, the 40 Ω digital output resistance and the virtual resistance meter's external-voltage threshold above 50 mV are model assumptions. The bench also checks peaks when one analog output is varying. With two varying outputs, it checks the computed sample times. Protection shutdown and stopping an analog output are model actions. Actual component damage and how a physical myDAQ behaves when its task stops remain unverified. [NI myDAQ Device Specifications, pp. 1 and 3–6](https://download.ni.com/support/manuals/373061g.pdf) · [NI on analog output after a task stops](https://knowledge.ni.com/KnowledgeArticleDetails?id=kA00Z000000g05ZSAQ&l=en-US)

#### Exercise

| Part | Practice on the bench |
| --- | --- |
| 5.1 | Locate terminals, BNC connectors, DMM jacks and jumpers. Use the function generator and waveform graph as virtual soft front panels. |
| 5.2 | Wire a test resistor and measure it in LabVIEW. Use the extra resistor to check your threshold logic. |
| 5.3 | Set up analog output as a signal source, wire the signal to PFI1 / DIO1 and measure using `myDAQ1/ctr0` in LabVIEW. |
| 5.4 | Read analog blocks and process them using the required Express VIs. |
| 5.5 | Read the potentiometer in LabVIEW and compute the digital port pattern. Close J7 for the LED display. |

The bench starts with the leads disconnected. Audio I/O, DMM current measurement and USB protocol are outside the exercise model.

The original NI DAQ Assistant supports channel selection and task setup with an NI MAX simulated device. NI-DAQmx generates its own readings in that mode. Use the included PuT-3 VIs to get readings from the wiring bench. [NI on simulated DAQmx devices](https://knowledge.ni.com/KnowledgeArticleDetails?id=kA00Z0000019Nw0SAE&l=en-US)

#### Verification

Run the [PowerShell self-test command](#tests) from the repository root. It switches to the `PuT-3` directory for the C# and LabVIEW checks. The C# tests cover wiring, connector hit areas, cable faults, resistance, overload and window layout. The native LabVIEW tests exercise requests and data exchange against the running bench. With LabVIEW 2026 and its local VI server on port `48372`, run `python tools/build_vis.py --cli` and `python tools/build_vis.py --express-run` to generate the test VIs through LabVIEW MCP, then run `python tools/test_labview.py`. Palette insertion and manual interaction with the Express configuration dialog remain outside the automated tests.
