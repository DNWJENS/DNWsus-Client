// Suche, Filter und Auswahl für die Client-Tabelle (rein im Browser, ohne neue Anfrage an den Server).
(function () {
    var table = document.getElementById("clients");
    if (table) {
        var search = document.getElementById("search");
        var hideStale = document.getElementById("hideStale");
        var counter = document.getElementById("visibleCount");
        var selectAll = document.getElementById("selectAll");
        var rows = table.tBodies[0].rows;

        try { hideStale.checked = localStorage.getItem("dnwsus.hideStale") === "1"; } catch (e) { }

        var apply = function () {
            var terms = search.value.toLowerCase().split(/\s+/).filter(Boolean);
            var visible = 0;
            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var text = row.getAttribute("data-search");
                var show = terms.every(function (t) { return text.indexOf(t) >= 0; }) &&
                           !(hideStale.checked && row.getAttribute("data-stale") === "1");
                row.style.display = show ? "" : "none";
                if (!show) row.querySelector("input[name=selected]").checked = false;
                if (show) visible++;
            }
            counter.textContent = visible + " von " + rows.length + " angezeigt";
        };

        search.addEventListener("input", apply);
        hideStale.addEventListener("change", function () {
            try { localStorage.setItem("dnwsus.hideStale", hideStale.checked ? "1" : "0"); } catch (e) { }
            apply();
        });
        selectAll.addEventListener("change", function () {
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].style.display !== "none")
                    rows[i].querySelector("input[name=selected]").checked = selectAll.checked;
            }
        });
        apply();
    }

    // Zeitplan-Formular: Wochentage oder Patchday-Versatz anzeigen
    var kind = document.getElementById("scheduleKind");
    if (kind) {
        kind.addEventListener("change", function () {
            var patchday = kind.value === "patchday";
            document.getElementById("weekdays").hidden = patchday;
            document.getElementById("patchdayOffset").hidden = !patchday;
        });
    }
})();

// Vor Aktionen auf ausgewählten Clients prüfen, ob überhaupt etwas ausgewählt ist.
function confirmSelected(question) {
    var count = document.querySelectorAll("#clients input[name=selected]:checked").length;
    if (count === 0) { alert("Bitte zuerst Clients auswählen."); return false; }
    return question === null || confirm(question + " (" + count + " ausgewählt)");
}
