// Suche und Filter für die Client-Tabelle (rein im Browser, ohne neue Anfrage an den Server).
(function () {
    var table = document.getElementById("clients");
    if (!table) return;

    var search = document.getElementById("search");
    var hideStale = document.getElementById("hideStale");
    var counter = document.getElementById("visibleCount");
    var rows = table.tBodies[0].rows;

    try { hideStale.checked = localStorage.getItem("dnwsus.hideStale") === "1"; } catch (e) { }

    function apply() {
        var terms = search.value.toLowerCase().split(/\s+/).filter(Boolean);
        var visible = 0;
        for (var i = 0; i < rows.length; i++) {
            var row = rows[i];
            var text = row.getAttribute("data-search");
            var show = terms.every(function (t) { return text.indexOf(t) >= 0; }) &&
                       !(hideStale.checked && row.getAttribute("data-stale") === "1");
            row.style.display = show ? "" : "none";
            if (show) visible++;
        }
        counter.textContent = visible + " von " + rows.length + " angezeigt";
    }

    search.addEventListener("input", apply);
    hideStale.addEventListener("change", function () {
        try { localStorage.setItem("dnwsus.hideStale", hideStale.checked ? "1" : "0"); } catch (e) { }
        apply();
    });
    apply();
})();
