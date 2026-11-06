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
                    defineTheme(window.monaco);
                    registerProvider(window.monaco);
                    registerCSharpProvider(window.monaco);
                    resolve(window.monaco);
                });
            });
        }
        return monacoPromise;
    }

    // Tema del editor: «Dark Modern» de VS Code (fondo #1f1f1f, selección #264f78, números de línea grises).
    function defineTheme(monaco) {
        monaco.editor.defineTheme("lr-dark-modern", {
            base: "vs-dark",
            inherit: true,
            rules: [],
            colors: {
                "editor.background": "#1f1f1f",
                "editor.foreground": "#cccccc",
                "editorLineNumber.foreground": "#6e7681",
                "editorLineNumber.activeForeground": "#cccccc",
                "editor.selectionBackground": "#264f78",
                "editor.inactiveSelectionBackground": "#3a3d41",
                "editor.lineHighlightBackground": "#282828",
                "editor.lineHighlightBorder": "#282828",
                "editorCursor.foreground": "#aeafad",
                "editorIndentGuide.background1": "#404040",
                "editorWidget.background": "#202020",
                "editorWidget.border": "#454545",
                "editorSuggestWidget.background": "#202020",
                "editorSuggestWidget.border": "#454545",
                "editorSuggestWidget.selectedBackground": "#04395e",
                "editorSuggestWidget.highlightForeground": "#2aaaff",
                "editorHoverWidget.background": "#202020",
                "editorHoverWidget.border": "#454545",
                "editorGutter.background": "#1f1f1f",
                "minimap.background": "#1f1f1f",
                "scrollbarSlider.background": "#79797966",
                "scrollbarSlider.hoverBackground": "#646464b3",
                "scrollbarSlider.activeBackground": "#bfbfbf66",
            },
        });
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

    // Métodos LINQ habituales, con snippets para rellenar los argumentos.
    const LINQ_METHODS = [
        ["Where", "Where(r => $1)"],
        ["Select", "Select(r => $1)"],
        ["OrderBy", "OrderBy(r => $1)"],
        ["OrderByDescending", "OrderByDescending(r => $1)"],
        ["GroupBy", "GroupBy(r => $1)"],
        ["Take", "Take(${1:30})"],
        ["Skip", "Skip(${1:0})"],
        ["Distinct", "Distinct()"],
        ["First", "First()"],
        ["FirstOrDefault", "FirstOrDefault()"],
        ["Count", "Count()"],
        ["Any", "Any(r => $1)"],
        ["ToList", "ToList()"],
        ["ToListAsync", "ToListAsync()"],
    ];

    // Autocompletado de C# para el script de EF Core. No es un análisis de tipos: usa los metadatos de la conexión.
    function registerCSharpProvider(monaco) {
        const K = monaco.languages.CompletionItemKind;
        const SNIPPET = monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet;

        // Tipo de Roslyn -> tipo de Monaco.
        const ROSLYN_KINDS = {
            Method: K.Method, ExtensionMethod: K.Method, Property: K.Property, Field: K.Field, Event: K.Event,
            Class: K.Class, Struct: K.Struct, Interface: K.Interface, Enum: K.Enum, EnumMember: K.EnumMember,
            Delegate: K.Function, Namespace: K.Module, Module: K.Module, Local: K.Variable, Parameter: K.Variable,
            RangeVariable: K.Variable, Constant: K.Constant, Keyword: K.Keyword, Snippet: K.Snippet,
            TypeParameter: K.TypeParameter, Label: K.Reference,
        };

        function roslynSuggestion(item, range) {
            const isMethod = item.kind === "Method" || item.kind === "ExtensionMethod";
            return {
                label: item.label,
                kind: ROSLYN_KINDS[item.kind] ?? K.Text,
                insertText: isMethod ? `${item.insertText}($1)` : item.insertText,
                insertTextRules: isMethod ? SNIPPET : undefined,
                range,
                sortText: item.sortText || item.label,
            };
        }

        monaco.languages.registerCompletionItemProvider("csharp", {
            triggerCharacters: [".", "\"", "["],
            provideCompletionItems: async (model, position) => {
                const ctx = byModel.get(model.uri.toString());
                if (!ctx) return { suggestions: [] };

                const line = model.getLineContent(position.lineNumber).slice(0, position.column - 1);
                const word = model.getWordUntilPosition(position);
                const wordRange = new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn);
                // Dentro de comillas: el rango va desde la comilla abierta hasta el cursor.
                const quoteRange = () => new monaco.Range(
                    position.lineNumber, line.lastIndexOf('"') + 2, position.lineNumber, position.column);
                const suggestions = [];

                // 0) Roslyn (en C#): conoce tipos, lambdas y métodos. Si no devuelve nada, se usan las heurísticas.
                try {
                    const items = await ctx.dotNet.invokeMethodAsync("Complete", model.getValue(), model.getOffsetAt(position));
                    if (items && items.length > 0) {
                        return { suggestions: items.map((i) => roslynSuggestion(i, wordRange)) };
                    }
                } catch (e) {
                    // Sin Roslyn se sigue con las heurísticas.
                }

                // 1) Dentro de Set<...>("...") → tablas esquema.nombre
                if (/Set<[^>]*>\(\s*"[^"]*$/.test(line)) {
                    for (const t of ctx.tables) {
                        const full = ctx.schema ? `${ctx.schema}.${t}` : t;
                        suggestions.push({ label: full, kind: K.Class, detail: "tabla", insertText: full, range: quoteRange() });
                    }
                    return { suggestions };
                }

                // 2) Dentro de r["..."] → columnas de la tabla que aparece en el script
                if (/\["[^"]*$/.test(line)) {
                    const table = currentTable(model.getValue(), ctx.schema);
                    if (table) {
                        for (const c of await getColumns(ctx, table)) {
                            suggestions.push({ label: c.name, kind: K.Field, detail: c.dataType, insertText: c.name, range: quoteRange() });
                        }
                    }
                    return { suggestions };
                }

                // 3) db. → tablas del esquema (cada una es un DbSet del contexto generado)
                if (/\bdb\.\w*$/.test(line)) {
                    for (const t of ctx.tables) {
                        suggestions.push({ label: t, kind: K.Property, detail: "tabla (DbSet)", insertText: t, range: wordRange });
                    }
                    return { suggestions };
                }

                // 4) .algo → métodos LINQ
                if (/\.\w*$/.test(line)) {
                    for (const [label, snippet] of LINQ_METHODS) {
                        suggestions.push({ label, kind: K.Method, detail: "LINQ", insertText: snippet, insertTextRules: SNIPPET, range: wordRange });
                    }
                    return { suggestions };
                }

                // 5) General: db, plantillas de script
                suggestions.push(
                    { label: "db", kind: K.Variable, detail: "contexto de la conexión", insertText: "db", range: wordRange },
                    { label: "var query", kind: K.Snippet, detail: "consulta", insertText: "var query = $1;", insertTextRules: SNIPPET, range: wordRange },
                    { label: "return query", kind: K.Snippet, detail: "devolver la consulta", insertText: "return query;", range: wordRange },
                );
                return { suggestions };
            },
        });
    }

    /// <summary>Tabla usada en el script: la última de db.Set&lt;...&gt;("esquema.tabla") del texto.</summary>
    function currentTable(text, schema) {
        const re = /Set<[^>]*>\(\s*"([^"]+)"/g;
        let last = null;
        let m;
        while ((m = re.exec(text)) !== null) last = m[1];
        if (!last) return null;
        const parts = last.split(".");
        return parts[parts.length - 1];
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
            theme: "lr-dark-modern",
        });

        const ctx = { id, editor, model, dotNet, tables: [], columns: new Map() };
        contexts.set(id, ctx);
        byModel.set(model.uri.toString(), ctx);

        // Ctrl+Enter ejecuta la consulta
        // addAction (no addCommand): el atajo solo actúa con este editor enfocado. Con addCommand, los atajos
        // de todos los editores comparten un único registro global y gana el último creado (quizá ya desmontado).
        editor.addAction({
            id: `run-query-${id}`,
            label: "Ejecutar consulta",
            keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter],
            run: () => { dotNet.invokeMethodAsync("Run", editor.getValue()); },
        });

        return id;
    }

    function setTables(id, tables, schema) {
        const ctx = contexts.get(id);
        if (!ctx) return;
        ctx.tables = tables || [];
        ctx.schema = schema || "";
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
