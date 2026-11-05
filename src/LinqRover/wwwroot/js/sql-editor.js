// Editor SQL sobre Monaco con autocompletado de tablas, columnas y palabras clave.
// Las columnas se piden a C# de forma perezosa (y se cachean) mediante DotNetObjectReference.
window.sqlEditor = (function () {
    const KEYWORDS = [
        "SELECT", "FROM", "WHERE", "JOIN", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "ON", "AND", "OR", "NOT",
        "IN", "IS", "NULL", "LIKE", "BETWEEN", "EXISTS", "GROUP BY", "ORDER BY", "HAVING", "DISTINCT",
        "AS", "TOP", "LIMIT", "OFFSET", "FETCH", "ROWS", "ONLY", "UNION", "ALL", "CASE", "WHEN", "THEN",
        "ELSE", "END", "COUNT", "SUM", "AVG", "MIN", "MAX", "WITH", "ASC", "DESC", "INSERT", "UPDATE",
        "DELETE", "VALUES", "SET", "CREATE", "ALTER", "DROP", "ROWNUM", "SYSDATE", "GETDATE", "NOLOCK",
    ];

    // Palabras que pueden aparecer tras FROM/JOIN sin ser alias.
    const NOT_ALIAS = new Set(KEYWORDS.map((k) => k.toUpperCase()).concat(["INNER", "LEFT", "RIGHT", "OUTER", "CROSS", "WHERE"]));

    let monacoPromise = null;
    let providerRegistered = false;
    let nextId = 1;
    const contexts = new Map();   // id -> contexto del editor
    const byModel = new Map();    // uri del modelo -> contexto

    function loadMonaco() {
        if (!monacoPromise) {
            monacoPromise = new Promise((resolve) => {
                window.require.config({ paths: { vs: "lib/monaco/vs" } });
                window.require(["vs/editor/editor.main"], () => {
                    registerProvider(window.monaco);
                    resolve(window.monaco);
                });
            });
        }
        return monacoPromise;
    }

    // Tablas y alias que aparecen tras FROM / JOIN en el texto completo.
    function parseReferences(text) {
        const refs = new Map(); // alias o nombre (minúsculas) -> nombre de tabla
        const re = /\b(?:FROM|JOIN)\s+(\[[^\]]+\]|"[^"]+"|[A-Za-z_][\w$#.]*)(?:\s+(?:AS\s+)?([A-Za-z_][\w$#]*))?/gi;
        let m;
        while ((m = re.exec(text)) !== null) {
            const table = m[1].replace(/^[\["]|[\]"]$/g, "").split(".").pop();
            refs.set(table.toLowerCase(), table);
            if (m[2] && !NOT_ALIAS.has(m[2].toUpperCase())) {
                refs.set(m[2].toLowerCase(), table);
            }
        }
        return refs;
    }

    async function getColumns(ctx, table) {
        if (!ctx.columns.has(table)) {
            ctx.columns.set(table, ctx.dotNet.invokeMethodAsync("GetColumnsAsync", table).catch(() => []));
        }
        return ctx.columns.get(table);
    }

    function registerProvider(monaco) {
        if (providerRegistered) return;
        providerRegistered = true;

        monaco.languages.registerCompletionItemProvider("sql", {
            triggerCharacters: [".", " "],
            provideCompletionItems: async (model, position) => {
                const ctx = byModel.get(model.uri.toString());
                if (!ctx) return { suggestions: [] };

                const word = model.getWordUntilPosition(position);
                const range = new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn);
                const fullText = model.getValue();
                const before = model.getValueInRange({
                    startLineNumber: 1, startColumn: 1,
                    endLineNumber: position.lineNumber, endColumn: position.column,
                });
                const refs = parseReferences(fullText);
                const suggestions = [];

                // "alias." o "tabla." -> columnas de esa tabla
                const dot = /([A-Za-z_][\w$#]*)\.\w*$/.exec(before);
                if (dot) {
                    const table = refs.get(dot[1].toLowerCase()) || dot[1];
                    const cols = await getColumns(ctx, table);
                    for (const c of cols) {
                        suggestions.push({
                            label: c.name, kind: monaco.languages.CompletionItemKind.Field,
                            detail: c.dataType, insertText: c.name, range, sortText: "0" + c.name,
                        });
                    }
                    return { suggestions };
                }

                // Tablas del esquema
                for (const t of ctx.tables) {
                    suggestions.push({
                        label: t, kind: monaco.languages.CompletionItemKind.Class,
                        detail: "tabla", insertText: t, range, sortText: "1" + t,
                    });
                }

                // Columnas de las tablas que aparecen en la consulta
                const unique = [...new Set(refs.values())];
                for (const table of unique) {
                    for (const c of await getColumns(ctx, table)) {
                        suggestions.push({
                            label: c.name, kind: monaco.languages.CompletionItemKind.Field,
                            detail: `${table} · ${c.dataType}`, insertText: c.name, range, sortText: "2" + c.name,
                        });
                    }
                }

                for (const k of KEYWORDS) {
                    suggestions.push({
                        label: k, kind: monaco.languages.CompletionItemKind.Keyword,
                        insertText: k, range, sortText: "3" + k,
                    });
                }

                return { suggestions };
            },
        });
    }

    async function create(element, dotNet, initialValue, language) {
        const monaco = await loadMonaco();
        const id = "sql-" + nextId++;
        const lang = language || "sql";
        const ext = lang === "csharp" ? "cs" : "sql";
        const model = monaco.editor.createModel(
            initialValue || "", lang, monaco.Uri.parse(`inmemory://query/${id}.${ext}`));

        const editor = monaco.editor.create(element, {
            model,
            automaticLayout: true,
            minimap: { enabled: false },
            fontFamily: "Cascadia Code, Consolas, monospace",
            fontSize: 13,
            wordWrap: "on",
            scrollBeyondLastLine: false,
            tabSize: 2,
            theme: document.documentElement.dataset.theme === "dark" ? "vs-dark" : "vs",
        });

        const ctx = { id, editor, model, dotNet, tables: [], columns: new Map() };
        contexts.set(id, ctx);
        byModel.set(model.uri.toString(), ctx);

        // Ctrl+Enter ejecuta la consulta
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => {
            dotNet.invokeMethodAsync("Run", editor.getValue());
        });

        return id;
    }

    function setTables(id, tables) {
        const ctx = contexts.get(id);
        if (!ctx) return;
        ctx.tables = tables || [];
        ctx.columns.clear(); // al cambiar de esquema, las columnas cacheadas ya no valen
    }

    function getValue(id) {
        const ctx = contexts.get(id);
        return ctx ? ctx.editor.getValue() : "";
    }

    function setValue(id, text) {
        const ctx = contexts.get(id);
        if (ctx) ctx.editor.setValue(text || "");
    }

    function dispose(id) {
        const ctx = contexts.get(id);
        if (!ctx) return;
        byModel.delete(ctx.model.uri.toString());
        ctx.editor.dispose();
        ctx.model.dispose();
        contexts.delete(id);
    }

    return { create, setTables, getValue, setValue, dispose };
})();
