// Divisor vertical: arrastrar la barra cambia la altura del panel de abajo.
// Durante el arrastre solo se toca el estilo del elemento; al soltar se avisa a C# (VerticalSplit.razor).
window.lrSplit = (function () {
    function register(handle, root, bottom, dotNet, minBottom, minTop) {
        handle.addEventListener("pointerdown", (e) => {
            if (e.button !== 0) return;
            e.preventDefault();
            handle.setPointerCapture(e.pointerId);
            // Mientras dura el arrastre, el cursor es de redimensionar en vertical aunque el puntero se salga de la barra.
            document.body.style.cursor = "n-resize";

            const startY = e.clientY;
            const startHeight = bottom.offsetHeight;
            let height = startHeight;

            const move = (ev) => {
                const max = root.clientHeight - handle.offsetHeight - minTop;
                height = Math.max(minBottom, Math.min(max, startHeight - (ev.clientY - startY)));
                bottom.style.height = height + "px";
            };

            const end = () => {
                document.body.style.cursor = "";
                handle.removeEventListener("pointermove", move);
                handle.removeEventListener("pointerup", end);
                handle.removeEventListener("pointercancel", end);
                dotNet.invokeMethodAsync("SetBottomHeight", Math.round(height));
            };

            handle.addEventListener("pointermove", move);
            handle.addEventListener("pointerup", end);
            handle.addEventListener("pointercancel", end);
        });
    }

    return { register };
})();
