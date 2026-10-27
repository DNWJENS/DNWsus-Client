// Suche, Filter und Auswahl für die Client-Tabelle (rein im Browser, ohne neue Anfrage an den Server).
(function () {
    var table = document.getElementById("clients");
    if (table) {
        var search = document.getElementById("search");
        var hideStale = document.getElementById("hideStale");
        var counter = document.getElementById("visibleCount");
        var selectAll = document.getElementById("selectAll");
        var onlyNoAgent = document.getElementById("onlyNoAgent");
        var rows = table.tBodies[0].rows;

        try { hideStale.checked = localStorage.getItem("dnwsus.hideStale") === "1"; } catch (e) { }

        var apply = function () {
            var terms = search.value.toLowerCase().split(/\s+/).filter(Boolean);
            var visible = 0;
            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var text = row.getAttribute("data-search");
                var show = terms.every(function (t) { return text.indexOf(t) >= 0; }) &&
                           !(hideStale.checked && row.getAttribute("data-stale") === "1") &&
                           !(onlyNoAgent.checked && row.getAttribute("data-noagent") !== "1");
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
        onlyNoAgent.addEventListener("change", apply);
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

// Sortierbare Spalten für alle .grid-Tabellen mit Kopfzeile. Klick auf einen Spaltenkopf sortiert,
// erneuter Klick dreht die Richtung. Zahlen/Datum über data-sort an der Zelle (sonst nach Text).
(function () {
    function cellValue(row, col) {
        var cell = row.cells[col];
        if (!cell) return "";
        var v = cell.getAttribute("data-sort");
        return v !== null ? v : cell.textContent.trim();
    }

    function sortTable(table, col, th) {
        var body = table.tBodies[0];
        var rows = Array.prototype.slice.call(body.rows);
        if (rows.length === 0) return;
        var asc = th.getAttribute("data-dir") !== "asc";

        var values = rows.map(function (r) { return cellValue(r, col).replace(",", "."); });
        var numeric = values.some(function (v) { return v !== ""; }) &&
                      values.every(function (v) { return v === "" || !isNaN(Number(v)); });

        rows.sort(function (a, b) {
            var x = cellValue(a, col).replace(",", "."), y = cellValue(b, col).replace(",", ".");
            if (numeric) {
                return asc ? (Number(x) || 0) - (Number(y) || 0) : (Number(y) || 0) - (Number(x) || 0);
            }
            return asc ? x.localeCompare(y, "de", { sensitivity: "base", numeric: true })
                       : y.localeCompare(x, "de", { sensitivity: "base", numeric: true });
        });

        rows.forEach(function (r) { body.appendChild(r); });

        var heads = th.parentNode.cells;
        for (var i = 0; i < heads.length; i++) {
            heads[i].removeAttribute("data-dir");
            heads[i].classList.remove("sorted-asc", "sorted-desc");
        }
        th.setAttribute("data-dir", asc ? "asc" : "desc");
        th.classList.add(asc ? "sorted-asc" : "sorted-desc");
    }

    document.querySelectorAll("table.grid").forEach(function (table) {
        if (!table.tHead || table.tBodies.length === 0) return;
        var headers = table.tHead.rows[0].cells;
        for (var c = 0; c < headers.length; c++) {
            var th = headers[c];
            if (th.classList.contains("sel")) continue; // Auswahl-Spalte nicht sortieren
            th.classList.add("sortable-col");
            (function (col, head) {
                head.addEventListener("click", function () { sortTable(table, col, head); });
            })(c, th);
        }
    });
})();

// Vor Aktionen auf ausgewählten Clients prüfen, ob überhaupt etwas ausgewählt ist.
function confirmSelected(question) {
    var count = document.querySelectorAll("#clients input[name=selected]:checked").length;
    if (count === 0) { alert("Bitte zuerst Clients auswählen."); return false; }
    return question === null || confirm(question + " (" + count + " ausgewählt)");
}

// Bulk-Soforthilfe: ausgewählte Clients ins Modal übernehmen, Neustart-Bedarf anzeigen.
function openBulk() {
    var checked = document.querySelectorAll("#clients input[name=selected]:checked");
    if (checked.length === 0) { alert("Bitte zuerst Clients auswählen."); return; }
    var container = document.getElementById("bulkSelected");
    container.innerHTML = "";
    var rebootCount = 0;
    checked.forEach(function (cb) {
        var inp = document.createElement("input");
        inp.type = "hidden"; inp.name = "selected"; inp.value = cb.value;
        container.appendChild(inp);
        var row = cb.closest("tr");
        if (row && row.getAttribute("data-reboot") === "1") rebootCount++;
    });
    document.getElementById("bulkSummary").textContent =
        checked.length + " Client(s) ausgewählt – " + rebootCount + " benötigen voraussichtlich einen Neustart.";
    document.getElementById("bulkModal").hidden = false;
}

function closeBulk() {
    document.getElementById("bulkModal").hidden = true;
}
