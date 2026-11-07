using System.Collections.Generic;
using OpenAT.Formats;

namespace OpenAT.View.Rooms.Planner
{
    /// <summary>
    /// OpenAT's own texts for the planner improvements (not in the original's text files). English and
    /// German; other install languages fall back to English. Placeholders: {0}, {1}, ...
    /// </summary>
    public static class PlannerTexts
    {
        static readonly Dictionary<string, (string en, string de)> T = new Dictionary<string, (string, string)>
        {
            ["fleet"] = ("Fleet", "Flotte"),
            ["search"] = ("Search name...", "Name suchen..."),
            ["count"] = ("{0} of {1}", "{0} von {1}"),
            ["none"] = ("No plane matches.", "Kein Flugzeug passt."),
            ["noplanes"] = ("No planes yet.", "Noch keine Flugzeuge."),
            ["clear"] = ("Reset", "Zurück"),
            ["type.all"] = ("All types", "Alle Typen"),
            ["status.all"] = ("Any state", "Jeder Status"),
            ["status.problem"] = ("Needs attention", "Probleme"),
            ["status.idle"] = ("Nothing planned", "Ohne Planung"),
            ["status.air"] = ("In the air", "In der Luft"),
            ["status.ground"] = ("On the ground", "Am Boden"),
            ["pin"] = ("Keep this schedule open beside the planner", "Flugplan neben dem Planer offen halten"),
            ["unpin"] = ("Close this schedule", "Flugplan schließen"),
            ["arrange"] = ("Arrange windows", "Fenster anordnen"),
            ["prev"] = ("Previous plane", "Voriges Flugzeug"),
            ["next"] = ("Next plane", "Nächstes Flugzeug"),
            ["pick"] = ("Choose a plane", "Flugzeug wählen"),
            ["filter.type"] = ("Filter by plane type", "Nach Flugzeugtyp filtern"),
            ["filter.status"] = ("Filter by state", "Nach Status filtern"),
            ["kind.route"] = ("Route flight", "Linienflug"),
            ["kind.contract"] = ("Charter order", "Auftragsflug"),
            ["kind.freight"] = ("Freight order", "Frachtflug"),
            ["kind.auto"] = ("Ferry flight", "Leerflug"),
            ["dep"] = ("Departs", "Abflug"),
            ["arr"] = ("Lands", "Landung"),
            ["gate"] = ("Gate {0}", "Gate {0}"),
            ["gate.none"] = ("No free gate at home!", "Kein freies Gate daheim!"),
            ["gate.away"] = ("Away from home airport", "Nicht am Heimatflughafen"),
            ["auto.why"] = ("Added automatically to reach the next departure city. Cannot be edited.",
                            "Automatisch eingefügt, um zur nächsten Abflugstadt zu kommen. Nicht änderbar."),
            ["locked"] = ("Starts too soon or already flying: locked.", "Startet zu bald oder fliegt schon: gesperrt."),
            ["howto"] = ("Drag an edge to move, click to pick up, Ctrl+click to set the time.",
                         "Kante ziehen: verschieben, Klick: aufnehmen, Strg+Klick: Zeit einstellen."),
            ["sel.title"] = ("Set departure", "Abflug einstellen"),
            ["sel.day"] = ("Day", "Tag"),
            ["sel.hour"] = ("Hour", "Uhrzeit"),
            ["sel.close"] = ("Right click or Esc: done", "Rechtsklick oder Esc: fertig"),
            ["res.placed"] = ("Planned: {0}", "Eingeplant: {0}"),
            ["res.moved"] = ("Moved to {0}", "Verschoben auf {0}"),
            ["res.req"] = ("{0} (wanted {1})", "{0} (gewünscht {1})"),
            ["res.ferry"] = ("{0} ferry flight(s) added", "{0} Leerflug/-flüge eingefügt"),
            ["res.ferrygone"] = ("{0} ferry flight(s) removed", "{0} Leerflug/-flüge entfernt"),
            ["res.shift"] = ("{0} other flight(s) moved", "{0} andere Flüge verschoben"),
            ["fail.outside-week"] = ("Only the coming seven days can be planned.", "Nur die nächsten sieben Tage sind planbar."),
            ["fail.too-late"] = ("This flight starts too soon to change.", "Dieser Flug startet zu bald für Änderungen."),
            ["fail.no-object"] = ("That order no longer exists.", "Diesen Auftrag gibt es nicht mehr."),
            ["fail.no-plane"] = ("That plane no longer exists.", "Dieses Flugzeug gibt es nicht mehr."),
            ["fail.bad-entry"] = ("That flight cannot be changed.", "Dieser Flug kann nicht geändert werden."),
            ["fail.other"] = ("Not possible: {0}", "Nicht möglich: {0}"),
            ["try"] = ("Try in planner", "Im Planer ausprobieren"),
            ["try.gone"] = ("This offer is no longer on the board.", "Dieses Angebot hängt nicht mehr aus."),
            ["fail.planned"] = ("This order is already planned or flown.", "Dieser Auftrag ist schon verplant oder geflogen."),
            ["try.pick"] = ("Click the tab of an offer to try it.", "Reiter eines Angebots anklicken, um es auszuprobieren."),
            ["try.place"] = ("Click an hour to try the flight there.", "Eine Stunde anklicken, um den Flug dort auszuprobieren."),
            ["try.move"] = ("Click the trial flight to move it.", "Probeflug anklicken, um ihn zu verschieben."),
            ["try.only"] = ("Only this plane and time were tried.", "Nur dieses Flugzeug und diese Zeit wurden geprüft."),
            ["try.flight"] = ("Trial", "Probe"),
            ["try.card"] = ("Trial flight: not in your plan yet.", "Probeflug: noch nicht im Plan."),
            ["try.newferry"] = ("Ferry flight added by this trial, to reach the next departure city.",
                                "Leerflug, den diese Probe einfügt, um zur nächsten Abflugstadt zu kommen."),
            ["try.movedby"] = ("Moved by this trial (currently planned: {0}).", "Durch die Probe verschoben (bisher: {0})."),
            ["try.wanted"] = ("(wanted {0})", "(gewünscht {0})"),
            ["try.tons"] = ("Planned {0} t of {1} t, {2} t open", "{0} t von {1} t verplant, {2} t offen"),
            ["try.gates"] = ("{0} more flight(s) without a free gate", "{0} Flug/Flüge mehr ohne freies Gate"),
            ["try.cannot"] = ("Cannot take this offer now: {0}", "Angebot jetzt nicht annehmbar: {0}"),
            ["try.closed"] = ("the counter is closed.", "der Schalter ist geschlossen."),
            ["try.accepted"] = ("Taken. Not scheduled yet.", "Angenommen. Noch nicht eingeplant."),
            ["try.scheduled"] = ("Taken and scheduled.", "Angenommen und eingeplant."),
            ["try.apply"] = ("Apply plan", "Plan übernehmen"),
            ["try.takeapply"] = ("Take and plan", "Annehmen+planen"),
            ["try.nottaken"] = ("The offer could not be taken.", "Das Angebot konnte nicht angenommen werden."),
            ["pick.how"] = ("Choose the plane to try this offer with. Click the plane name in the title to come back here.",
                            "Flugzeug für dieses Angebot wählen. Klick auf den Namen im Titel führt hierher zurück."),
            ["pick.none"] = ("None of your planes can fly this offer.", "Keines Ihrer Flugzeuge kann dieses Angebot fliegen."),
            ["try.applied"] = ("The trial flights are in the plan now.", "Die Probeflüge sind jetzt eingeplant."),
            ["try.notapplied"] = ("Not applied: {0} The order stays unscheduled; plan it in the planner.", "Nicht übernommen: {0} Der Auftrag bleibt unverplant; bitte im Planer einplanen."),
            ["try.changed"] = ("the plans changed since the trial.", "die Planung hat sich seit der Probe geändert."),
            ["try.replaces"] = ("This would take another flight off the plan.", "Das würde einen anderen Flug aus dem Plan nehmen."),
            ["try.nolegs"] = ("nothing to apply.", "nichts zu übernehmen."),
            ["try.error"] = ("The trial could not be computed.", "Die Probe konnte nicht berechnet werden."),
            ["close"] = ("Close", "Schließen"),
            ["money.inout"] = ("Income {0}, costs {1}", "Einnahmen {0}, Kosten {1}"),
            ["money.profit"] = ("Expected profit: {0}", "Erwarteter Gewinn: {0}"),
            ["money.loss"] = ("Expected loss: {0}", "Erwarteter Verlust: {0}"),
            ["plane.range"] = ("This plane cannot fly that far.", "Dieses Flugzeug fliegt nicht so weit."),
            ["plane.small"] = ("This plane is too small.", "Dieses Flugzeug ist zu klein."),
        };

        public static TextResource.Language Language = TextResource.Language.English;

        public static string Get(string key, params object[] args)
        {
            if (!T.TryGetValue(key, out var v)) return key;
            string s = Language == TextResource.Language.German ? v.de : v.en;
            return args == null || args.Length == 0 ? s : string.Format(s, args);
        }
    }
}
