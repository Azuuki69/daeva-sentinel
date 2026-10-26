using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BakarmaCoach;

/// <summary>UI strings only. Game skill, boss, dungeon, class, item and player names never enter this table.</summary>
public static class UiLanguage
{
    public static string Current { get; set; } = "en";
    private static readonly Dictionary<string, string> Polish = new()
    {
        ["Battle companion"] = "Asystent walki", ["Encounter profiles"] = "Profile walki", ["Shared experimental rules for all three modes."] = "Wspólne, eksperymentalne reguły dla trzech poziomów trudności.",
        ["Game names stay in their original language."] = "Nazwy z gry pozostają w oryginalnym języku.", ["Optional and unverified. Disabled by default."] = "Opcjonalne i niezweryfikowane. Domyślnie wyłączone.",
        ["Two sources. One warning."] = "Dwa źródła. Jedno ostrzeżenie.", ["Network events supply timing. Screen OCR confirms visible skill names. Duplicate callouts are suppressed."] = "Sieć dostarcza informacje o czasie, a OCR odczytuje widoczne nazwy umiejętności. Powtórzone ostrzeżenia są pomijane.",
        ["Local recognition · screenshots stay in memory"] = "Odczyt lokalny · obrazy pozostają w pamięci", ["English voice, always."] = "Głos zawsze po angielsku.",
        ["Interface language changes the controls, not the spoken callouts or game names."] = "Zmiana języka dotyczy interfejsu. Głos i nazwy z gry pozostają po angielsku.",
        ["Your companion in Atreia."] = "Twój towarzysz w Atreia.", ["Ready for the next encounter."] = "Gotowy na kolejną walkę.",
        ["Hear the mechanic. Make your move."] = "Usłysz ostrzeżenie. Zareaguj na czas.",
        ["Transcendent Bakarma · Draupnir · Local voice alerts"] = "Transcendent Bakarma · Draupnir · Lokalne ostrzeżenia głosowe",
        ["Test module: Transcendent Bakarma · Draupnir"] = "Moduł testowy: Transcendent Bakarma · Draupnir",
        ["READY TO TEST"] = "GOTOWY DO TESTU", ["Your next warning appears here."] = "Tutaj pojawi się następne ostrzeżenie.",
        ["No live fight connected"] = "Nie wykryto aktywnej walki", ["DIFFICULTY"] = "POZIOM TRUDNOŚCI", ["VOICE"] = "GŁOS",
        ["Volume"] = "Głośność", ["Speak mechanic warnings"] = "Odtwarzaj ostrzeżenia głosowe", ["Speak cast-end countdown: 3, 2, 1"] = "Odliczaj koniec rzucania: 3, 2, 1",
        ["OPTIONAL HP PREPARATION"] = "OPCJONALNE PRZYPOMNIENIA WEDŁUG HP", ["Enable unverified HP reminders"] = "Włącz niezweryfikowane przypomnienia HP",
        ["Conquest starts with Game8's 75,25 as a suggestion. Thresholds vary across guides and difficulties. Reminders are off by default."] = "Dla Conquest sugerowane progi z Game8 to 75,25. Poradniki i poziomy trudności podają różne wartości. Przypomnienia są domyślnie wyłączone.",
        ["Countdown means cast end, not wave arrival. Individual jump timing is not predicted."] = "Odliczanie oznacza koniec rzucania, a nie nadejście fali. Program nie przewiduje momentu pojedynczych skoków.",
        ["INPUT SOURCES"] = "ŹRÓDŁA DANYCH", ["Network events"] = "Zdarzenia z sieci", ["Screen capture + English OCR"] = "Przechwytywanie ekranu + angielski OCR",
        ["Calibrate screen"] = "Skalibruj ekran", ["Preview screen reading"] = "Podgląd odczytu ekranu", ["Screen input needs calibration: boss name + ability name."] = "Wymagana kalibracja: nazwa bossa i nazwa umiejętności.",
        ["Screen calibrated · ready to read boss and ability names"] = "Ekran skalibrowany · gotowy do odczytu nazw bossa i umiejętności",
        ["OVERLAY VIEW"] = "WIDOK NAKŁADKI", ["Reset meters"] = "Wyzeruj liczniki", ["Boss alerts"] = "Ostrzeżenia bossa", ["DPS meter"] = "Licznik DPS", ["Threat / boss target"] = "Zagrożenie / cel bossa",
        ["DPS = recorded party damage / elapsed time. Threat bars are a damage-only proxy, not server enmity; taunts, tank bonuses and healing threat are not modeled."] = "DPS = zarejestrowane obrażenia drużyny / czas walki. Paski zagrożenia porównują wyłącznie obrażenia; nie odzwierciedlają rzeczywistego aggro, prowokacji, bonusów tanków ani leczenia.",
        ["Start live alerts"] = "Włącz ostrzeżenia", ["Play voice demo"] = "Odtwórz demo", ["Test voice"] = "Przetestuj głos", ["Show overlay"] = "Pokaż nakładkę", ["Hide overlay"] = "Ukryj nakładkę", ["Stop / reset"] = "Zatrzymaj / wyzeruj",
        ["Live capture requires Npcap. The demo works without the game."] = "Odczyt sieci wymaga Npcap. Demo działa bez uruchomionej gry.",
        ["Preparing local voice clips…"] = "Przygotowywanie lokalnych nagrań…", ["English voice pack ready · no internet needed during combat"] = "Angielski głos gotowy · internet nie jest wymagany do odtwarzania",
        ["Ctrl+Alt+B: show/hide · Ctrl+Alt+M: mute · Ctrl+Alt+1/2/3: Boss/DPS/Threat. Shared experimental boss rules; live accuracy still needs verification."] = "Ctrl+Alt+B: pokaż/ukryj · Ctrl+Alt+M: wycisz · Ctrl+Alt+1/2/3: Boss/DPS/Zagrożenie. Reguły bossa są eksperymentalne i wymagają weryfikacji w grze.",
        ["RECENT CALLOUTS"] = "OSTATNIE OSTRZEŻENIA", ["Local mechanic logs: data/logs · No combat uploads · Audio uses your Windows default output"] = "Lokalne logi: data/logs · Bez wysyłania walk · Dźwięk korzysta z domyślnego wyjścia Windows",
        ["STOPPED"] = "ZATRZYMANO", ["Stopped"] = "Zatrzymano", ["Ready for the next pull."] = "Gotowy do kolejnej walki.",
        ["Capture stopped. Choose live alerts or play a synthetic demo."] = "Odczyt zatrzymany. Włącz ostrzeżenia lub uruchom demo.",
        ["LIVE · EXPERIMENTAL"] = "NA ŻYWO · EKSPERYMENTALNE", ["LIVE · HYBRID"] = "NA ŻYWO · DWA ŹRÓDŁA", ["LIVE · SCREEN"] = "NA ŻYWO · EKRAN", ["LIVE · NETWORK"] = "NA ŻYWO · SIEĆ", ["CAPTURE UNAVAILABLE"] = "ODCZYT NIEDOSTĘPNY",
        ["Listening for Bakarma's casts."] = "Oczekiwanie na umiejętności Bakarma.", ["DEMO · SYNTHETIC EVENTS"] = "DEMO · DANE TESTOWE",
        ["A 26-second demonstration, not Bakarma's real attack rotation. No capture is running."] = "26-sekundowa demonstracja z danymi testowymi. To nie jest rzeczywista rotacja Bakarma. Odczyt jest wyłączony.",
        ["Demo begins in a moment…"] = "Demo rozpocznie się za chwilę…", ["Demo finished. Live accuracy still needs an in-game test."] = "Demo zakończone. Dokładność wymaga testu podczas prawdziwej walki.",
        ["No active cast"] = "Brak aktywnej umiejętności", ["Stagger window detected."] = "Wykryto okno na stagger.", ["Watching for the next mechanic…"] = "Oczekiwanie na następną mechanikę…",
        ["Move in · jump the waves"] = "Podejdź · przeskakuj fale", ["Watch the frontal attack"] = "Uważaj na atak frontalny", ["Get behind Bakarma"] = "Stań za Bakarma",
        ["Sidestep the frontal charge"] = "Zejdź z linii ataku", ["Dodge incoming projectiles"] = "Unikaj nadlatujących pocisków", ["Bomb on you · keep moving"] = "Bomba na tobie · nie stój",
        ["Water bombs · watch the ground"] = "Bomby · obserwuj podłoże", ["Area attack · watch the ground"] = "Atak obszarowy · uważaj", ["Waves soon · save stagger"] = "Wkrótce fale · zachowaj stagger",
        ["Use stagger skills · now"] = "Użyj umiejętności stagger · teraz", ["Stagger broken · attack"] = "Stagger przełamany · atakuj", ["Waiting for next mechanic"] = "Oczekiwanie na mechanikę", ["Waiting for Bakarma"] = "Oczekiwanie na Bakarma",
        ["DAMAGE PER SECOND  /  PARTY"] = "OBRAŻENIA NA SEKUNDĘ  /  DRUŻYNA", ["ESTIMATED THREAT"] = "SZACOWANE ZAGROŻENIE", ["DAMAGE PROXY · NOT SERVER ENMITY"] = "PORÓWNANIE OBRAŻEŃ · NIE AGGRO SERWERA",
        ["Waiting for recorded party damage…"] = "Oczekiwanie na obrażenia drużyny…", ["Network events are required for this view."] = "Ten widok wymaga danych z sieci.", ["Waiting for Bakarma network events…"] = "Oczekiwanie na dane sieciowe Bakarma…",
        ["You"] = "Ty", ["Unknown"] = "Nieznany", ["Unknown player"] = "Nieznany gracz", ["Player"] = "Gracz", ["Lock"] = "Zablokuj", ["Drag to position"] = "Przeciągnij, aby przesunąć", ["◈  Boss"] = "◈  Boss", ["▥  DPS"] = "▥  DPS", ["◇  Threat"] = "◇  Zagrożenie",
        ["NET"] = "SIEĆ", ["SCREEN"] = "EKRAN", ["BOTH"] = "OBA", ["LIVE"] = "NA ŻYWO", ["LANGUAGE"] = "JĘZYK", ["AION 2 COMPANION"] = "ASYSTENT AION 2", ["OVERLAY VIEWS"] = "WIDOKI NAKŁADKI", ["ENGLISH VOICE"] = "ANGIELSKI GŁOS",
        ["Screen reading"] = "Odczyt ekranu", ["Encounter"] = "Walka z bossem", ["Threat estimate"] = "Szacowane zagrożenie", ["Most recent game capture"] = "Ostatni odczyt ekranu gry",
        ["Only the two selected game regions are read. Recognition is local; screenshots are kept in memory only. Recalibrate if the crop misses the names."] = "Odczytywane są tylko dwa wybrane obszary gry. Rozpoznawanie działa lokalnie, a obrazy pozostają w pamięci. Skalibruj ponownie, jeśli wycinek nie obejmuje nazw.",
        ["Waiting for a second frame"] = "Oczekiwanie na drugą klatkę", ["Screen preview"] = "Podgląd odczytu", ["Aion 2 not found"] = "Nie znaleziono Aion 2",
        ["Open Aion 2 in borderless/windowed mode, target Bakarma so his name is visible, then click Calibrate screen."] = "Uruchom Aion 2 w oknie lub trybie bez ramek. Zaznacz Bakarma, aby wyświetlić jego nazwę, a następnie kliknij Skalibruj ekran.",
        ["Start live alerts and bring Aion 2 to the foreground. The most recent captured text regions will be available here."] = "Włącz ostrzeżenia i przejdź do Aion 2. Tutaj pojawi się podgląd ostatnio odczytanych obszarów.",
        ["Calibrated. Click Start live alerts, then return to Aion 2."] = "Kalibracja zakończona. Włącz ostrzeżenia i wróć do Aion 2.",
        ["Click Calibrate screen first. Network alerts can still run."] = "Najpierw skalibruj ekran. Ostrzeżenia z sieci mogą działać niezależnie.",
        ["No input started. Enable a source and calibrate screen input if selected."] = "Nie uruchomiono odczytu. Wybierz źródło i w razie potrzeby skalibruj ekran.",
        ["Screen recognition active · network input is off or unavailable"] = "Rozpoznawanie ekranu aktywne · sieć jest wyłączona lub niedostępna",
        ["Screen paused · Aion 2 must be in the foreground"] = "Odczyt ekranu wstrzymany · przejdź do Aion 2", ["Game aspect ratio changed · calibrate screen again"] = "Zmieniono proporcje okna gry · ponownie skalibruj ekran",
        ["Stale frame discarded"] = "Pominięto nieaktualną klatkę", ["Repeated frame discarded"] = "Pominięto powtórzoną klatkę", ["No known cast name visible"] = "Nie widać znanej nazwy umiejętności", ["Waiting for Transcendent Bakarma's name"] = "Oczekiwanie na nazwę Transcendent Bakarma",
        ["Cast still visible"] = "Nazwa umiejętności nadal widoczna", ["Confirming cast on the next frame"] = "Potwierdzanie umiejętności w kolejnej klatce", ["Cast confirmed by two frames"] = "Umiejętność potwierdzona w dwóch klatkach",
        ["Waves incoming. Move toward Bakarma and jump."] = "Nadchodzą fale. Podejdź do Bakarma i przeskakuj je.", ["Frontal attack. Watch the boss."] = "Atak frontalny. Obserwuj bossa.", ["Frontal sweep. Get behind Bakarma."] = "Atak frontalny. Stań za Bakarma.",
        ["Frontal charge. Move out of the line."] = "Atak po linii. Zejdź z drogi.", ["Projectiles incoming. Watch the ground."] = "Nadchodzą pociski. Obserwuj podłoże.", ["Water bomb on you. Keep moving."] = "Bomba na tobie. Nie zatrzymuj się.", ["Water bombs incoming. Watch the ground."] = "Nadchodzą bomby. Obserwuj podłoże.",
        ["Area attack. Watch the ground."] = "Atak obszarowy. Obserwuj podłoże.", ["Wave phase soon. Save your stagger skills."] = "Wkrótce faza fal. Zachowaj umiejętności stagger.", ["Stagger now. Use your stagger skills."] = "Stagger teraz. Użyj odpowiednich umiejętności.", ["Stagger broken. Attack Bakarma."] = "Stagger przełamany. Atakuj Bakarma.",
        ["Launcher"] = "Launcher", ["Launch companion"] = "Uruchom asystenta", ["Check for updates"] = "Sprawdź aktualizacje", ["Update & launch"] = "Zaktualizuj i uruchom", ["Rollback"] = "Przywróć poprzednią wersję", ["Ready to play"] = "Gotowy do gry", ["Checking for updates…"] = "Sprawdzanie aktualizacji…", ["Downloading update…"] = "Pobieranie aktualizacji…", ["Verifying package…"] = "Weryfikowanie pakietu…", ["Installing update…"] = "Instalowanie aktualizacji…", ["Up to date"] = "Najnowsza wersja", ["Update installed"] = "Aktualizacja zainstalowana", ["Settings are preserved across updates."] = "Ustawienia są zachowywane podczas aktualizacji.", ["Signed updates · English voice · No subscription"] = "Podpisane aktualizacje · Angielski głos · Bez abonamentu", ["Open GitHub"] = "Otwórz GitHub", ["Installed version"] = "Zainstalowana wersja", ["Latest version"] = "Najnowsza wersja"
        ,["◈  Encounter"] = "◈  Walka z bossem", ["▥  DPS meter"] = "▥  Licznik DPS", ["◇  Threat estimate"] = "◇  Szacowane zagrożenie", ["⌁  Screen reading"] = "⌁  Odczyt ekranu"
        ,["English · bundled neural voice · en-US"] = "Angielski · dołączony głos neuronowy · en-US"
    };
    private static readonly (string En, string Pl)[] Prefixes =
    [
        ("Network unavailable: ", "Sieć niedostępna: "), ("Input paused after an error: ", "Odczyt wstrzymany z powodu błędu: "),
        ("Screen OCR unavailable: ", "OCR niedostępny: "), ("Calibration failed: ", "Błąd kalibracji: "),
        ("Could not save settings: ", "Nie można zapisać ustawień: "), ("Voice unavailable: ", "Głos niedostępny: "),
        ("Boss target  →  ", "Cel bossa  →  "), ("Boss: ", "Boss: "), ("Ability: ", "Umiejętność: "),
        ("Cast confirmed by two frames · ", "Umiejętność potwierdzona w dwóch klatkach · "),
        ("Cast still visible · ", "Nazwa umiejętności nadal widoczna · "), ("Confirming cast on the next frame · ", "Potwierdzanie umiejętności w kolejnej klatce · ")
    ];
    public static string English(string text)
    {
        text = text.Replace(" · koniec rzucania za ", " · cast ends in ", StringComparison.Ordinal);
        string original = Polish.FirstOrDefault(kv => kv.Value == text).Key ?? text;
        foreach (var pair in Prefixes) if (original.StartsWith(pair.Pl, StringComparison.Ordinal) && pair.Pl != pair.En) { original = pair.En + original[pair.Pl.Length..]; break; }
        return original;
    }
    public static string T(string text)
    {
        string original = English(text);
        if (Current != "pl") return original;
        if (original.Contains(" · cast ends in ", StringComparison.Ordinal)) return original.Replace(" · cast ends in ", " · koniec rzucania za ", StringComparison.Ordinal);
        if (Polish.TryGetValue(original, out string? translated)) return translated;
        foreach (var pair in Prefixes) if (original.StartsWith(pair.En, StringComparison.Ordinal)) return pair.Pl + original[pair.En.Length..];
        foreach (var pair in Polish) if (pair.Key.Length > 25 && original.EndsWith(pair.Key, StringComparison.Ordinal)) return original[..^pair.Key.Length] + pair.Value;
        return original;
    }
    public static void Apply(DependencyObject root)
    {
        Apply(root, new HashSet<DependencyObject>());
    }
    private static void Apply(DependencyObject root, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root)) return;
        if (root is TextBlock text && !Equals(text.Tag, "GameName")) text.Text = T(text.Text);
        if (root is ContentControl content && content.Content is string label) content.Content = T(label);
        if (root is HeaderedContentControl header && header.Header is string title) header.Header = T(title);
        if (root is FrameworkElement element && element.ToolTip is string tip) element.ToolTip = T(tip);
        if (root is Visual || root is System.Windows.Media.Media3D.Visual3D)
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Apply(VisualTreeHelper.GetChild(root, i), visited);
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject dependency) Apply(dependency, visited);
    }
}
