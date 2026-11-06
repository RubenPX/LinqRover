// Arrastre de tablas desde la lista lateral hasta una zona del editor visual.
// No usa el arrastre nativo de HTML5 (WebView2 no entrega los eventos dragover/drop dentro de MAUI);
// usa eventos de puntero, que sí llegan. Al soltar, se busca la zona que hay bajo el puntero y se le avisa.
window.lrDrag = (function () {
    const zones = new Map();      // elemento -> referencia .NET
    const DRAG_THRESHOLD = 5;     // píxeles que hay que mover antes de considerarlo un arrastre
    let pending = null;           // { table, x, y } mientras el botón está pulsado
    let dragging = false;
    let ghost = null;
    let highlighted = null;
    let suppressClick = false;

    function zoneAt(x, y) {
        const el = document.elementFromPoint(x, y);
        return el ? el.closest("[data-lr-drop]") : null;
    }

    function highlight(zone) {
        if (highlighted === zone) return;
        if (highlighted) highlighted.style.outline = "";
        highlighted = zone;
        if (highlighted) highlighted.style.outline = "2px dashed #0078d4";
    }

    function moveGhost(x, y) {
        ghost.style.transform = `translate(${x + 12}px, ${y + 8}px)`;
    }

    function down(e) {
        if (e.button !== 0) return;
        suppressClick = false;
        pending = { table: e.currentTarget.dataset.table, x: e.clientX, y: e.clientY };
    }

    function move(e) {
        if (!pending) return;
        if (!dragging) {
            if (Math.hypot(e.clientX - pending.x, e.clientY - pending.y) < DRAG_THRESHOLD) return;
            dragging = true;
            ghost = document.createElement("div");
            ghost.textContent = pending.table;
            ghost.style.cssText = "position:fixed;left:0;top:0;z-index:9999;pointer-events:none;" +
                "padding:2px 8px;border-radius:4px;background:#202020;color:#cccccc;border:1px solid #454545;font:12px Consolas,monospace;opacity:.95;";
            document.body.appendChild(ghost);
        }
        moveGhost(e.clientX, e.clientY);
        highlight(zoneAt(e.clientX, e.clientY));
    }

    async function up(e) {
        if (!pending) return;
        const info = pending;
        pending = null;
        if (!dragging) return;

        // Un clic que termina un arrastre no debe seleccionar la tabla.
        dragging = false;
        suppressClick = true;
        ghost.remove();
        ghost = null;

        const zone = zoneAt(e.clientX, e.clientY);
        highlight(null);
        const ref = zone && zones.get(zone);
        if (ref) await ref.invokeMethodAsync("OnTableDropped", info.table, e.clientX, e.clientY);
    }

    document.addEventListener("pointermove", move);
    document.addEventListener("pointerup", up);
    // Tras un arrastre, se bloquea el clic que genera el navegador al soltar.
    document.addEventListener("click", (e) => {
        if (suppressClick) {
            e.stopPropagation();
            e.preventDefault();
            suppressClick = false;
        }
    }, true);

    return {
        down,
        register(element, dotNetRef) {
            element.setAttribute("data-lr-drop", "");
            zones.set(element, dotNetRef);
        },
        unregister(element) {
            zones.delete(element);
            element.removeAttribute("data-lr-drop");
        },
    };
})();
