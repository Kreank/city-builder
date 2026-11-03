# Probelauf im Editor – Checkliste

Alles hier ist in der Simulation getestet (xUnit), aber **noch nie im Editor gesehen**. Bitte bei jedem Punkt kurz
notieren: passt / sieht komisch aus / Fehler (Console-Meldung oder Screenshot reicht).

Vorher: Szene `Assets/_Project/Scenes/Main.unity` öffnen. Falls Store-Assets fehlen: „City Builder → Third Party →
Store-Pakete …“ → „Importieren und einsortieren“. Console offen lassen (Window → General → Console).

## 1. Start & Oberfläche
- [ ] Play: dunkle UI erscheint (obere Leiste, linke Ansichtsleiste, Bauleiste unten). Keine roten Console-Fehler.
- [ ] Klicks auf Knöpfe kommen an (falls nicht: fehlendes EventSystem – bitte melden).
- [ ] Schrift lesbar (nicht dunkel auf dunkel), Sonderzeichen €, −, ×, ³ korrekt.
- [ ] Startmodus „Auswählen“: Linksklick baut nichts, wählt nur aus. Mauszeiger-Knopf ganz links in der Bauleiste.
- [ ] F1 zeigt die Hilfe mit allen Tasten.

## 2. Straßen
- [ ] Gerade/Kurve bauen, Kreuzung, 90°-Knick, enge Kurve: Bürgersteige ohne Überlappung, gerundete Bordsteine.
- [ ] Nahe einer Straße: gestrichelte Parallel-Hilfslinien, Beschriftung „Abstand 62,0 m · optimal (Rücken an Rücken …)“, Einrasten mit G.
- [ ] F9 (Gebäudetiefe 16/24/32 m/aus), F10 (Blockraster) ausprobieren. Esc/Rechtsklick → zurück zum Auswählen.

## 3. Bauen (6) – Setzen statt Zonen
- [ ] Taste 6 / Kategorie „Bauen“: Reiter Wohnen / Gewerbe / Industrie, je Bauart eine Kachel mit Nachfrage-Balken; Klick auf
      eine Kachel wechselt die Bauart, Reiter zeigen nur ihre Kacheln.
- [ ] Einzelklick an einer Stadtstraße setzt ein Gebäude (echtes Modell als Geist, grün/rot, Pfeil zur Straße), `,`/`.` drehen.
- [ ] **LMB halten und entlang der Straße ziehen:** Reihe gleicher Gebäude erscheint (gültige grün, ungültige rot), lückenlos,
      Vorderseite zur Straße, auch in Kurven und über einen Knick; Hinweiszeile „Reihe: 7 × Einfamilienhaus · Kosten …“.
      Loslassen baut alle grünen auf einmal (Stadtkasse M: **eine** Buchung „Bau Gebäude (gesetzt)“). RMB/Esc während des
      Ziehens bricht nur die Reihe ab.
- [ ] Laden/Büro/Halle setzen → im Inspektor Reiter „Firma“: Händler/Büro/Industriebetrieb (Betrieb wählt die Simulation).
      Mit Schwerpunkt (Z) über einem Vorkommen ziehen dort passende Betriebe ein.
- [ ] Gesetzte Gebäude stehen **ohne** farbige Grundstücksplatte; neues Spiel ohne Kategorie „Zonen“ und ohne Zonenraster.
- [ ] Obere Leiste: „Nachfrage“ mit 4 Balken W L B I, Tooltip je Balken mit Gründen (Arbeitslose, freie Jobs, Grundbedarf
      gedeckt, Importanteil …); Klick öffnet Bauen mit passendem Reiter.
- [ ] Z öffnet „Lenken“ (Schwerpunkt-Pinsel), ohne Zonenraster; erneut Z → Auswählen.
- [ ] Straßenbau neben einer Straße: Hilfslinie „Abstand 62,0 m · optimal (Rücken an Rücken, 2 × 24 m tief)“, nach Wahl der
      Bauart (z. B. Einfamilienhaus) 46 m; F9 schaltet 16 → 24 → 32 m → aus. Statt Zonenflächen nur eine zarte Linie an der Rückseite.
- [ ] Alter Spielstand mit Zonen: Kategorie „Zonen“ erscheint, Zonen wachsen weiter, Platten nur an Zonengebäuden.
- [ ] Straße durch eine Zone (alter Stand): Zone und Haus verschwinden dort (gewollt); gesetzte Gebäude sperren die Straße.

## 4. Versorgung (J) – selbst verlegen
- [ ] Kraftwerk → Hochspannung → Umspannwerk → Ortsnetz (Erdkabel oder Freileitung) → Häuser werden grün (O).
- [ ] Wasserturm/Pumpe → Wasserrohr; Abwasser: Kläranlage oder Auslass → Kanal.
- [ ] „Entlang Straßen verlegen“ für eine Straße und für „Alle Stadtstraßen“ (Kostenvorschau).
- [ ] Kraftwerke/Windrad: eigene Blender-Modelle sichtbar, Windrad-Rotor dreht sich, Gebäude richten sich zur Straße.
- [ ] Unterirdisch-Ansicht (X): Rohre, Kanäle, Hausanschlüsse sichtbar.

## 5. Dienste (4), ÖPNV (5)
- [ ] Polizei/Schule/Park platzieren: Vorderseite zeigt automatisch zur Straße, Reichweiten-Färbung der Straßen.
- [ ] Straße durch ein Dienstgebäude planen → rot „Straße überschneidet …“, Gebäude bleibt.
- [ ] Dienstgebäude auswählen → Y (Verschieben) → woanders absetzen.
- [ ] Haltestellen setzen, Linie zeichnen (Enter), Busdepot, F6 Linienfenster, Busse fahren und halten.

## 6. Menschen & Info
- [ ] Fußgänger auf Gehwegen, im Park, an Haltestellen; anklickbar.
- [ ] Haus/Auto/Bürger anklicken: Info-Panel rechts mit Reitern (Bedürfnisse, Persönlichkeit, Erinnerungen, Firma, Bodenwert).

## 7. Wirtschaft & Welt
- [ ] M Stadtkasse (Steuern ±), F5 Wirtschaft (Güter, Firmen, Lieferungen), F11 Handel (Länder, Angebote).
- [ ] F4 Weltkarte (Land anklicken), F3 Rohstoffe, F2 Bodenwert, Medien-Schlagzeilen oben rechts (mit Zeitung/Sender).

## 8. Speichern & Laden
- [ ] Esc → Spielmenü → Speichern, dann Laden: Stadt kommt identisch zurück (Straßen, Häuser, Autos, Busse).
- [ ] Strg+S / Strg+L, Autosave nach einigen Spieltagen.

## 9. Leistung
- [ ] Bei größerer Stadt Tempo 3: ruckelt es? (Profiler-Screenshot oder grobe FPS genügen.)
