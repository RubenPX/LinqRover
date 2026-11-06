// Copia Monaco Editor a wwwroot para que la app funcione sin conexión.
// Solo se copian los lenguajes que usa la app (SQL y C#), los servicios de lenguaje y los idiomas
// de la interfaz que no hacen falta (TypeScript, JSON, CSS, HTML y localizaciones distintas del español).
import { rmSync, existsSync, readFileSync, readdirSync, statSync, mkdirSync, copyFileSync } from "node:fs";
import { join, relative, sep } from "node:path";

const src = "node_modules/monaco-editor/min/vs";
const dst = "wwwroot/lib/monaco/vs";

// Lenguajes que se mantienen. Su fragmento se identifica por el id del registro en basic-languages.
const KEEP_LANGUAGES = new Set(["sql", "csharp"]);

if (!existsSync(src)) {
  console.error("Falta monaco-editor. Ejecuta `npm install` primero.");
  process.exit(1);
}

// Fragmentos (sin extensión) de los lenguajes que no se usan: ../abap-D-t0cyap, etc.
const contribution = readFileSync(join(src, "basic-languages", "monaco.contribution.js"), "utf8");
const keepChunks = new Set();
const dropChunks = new Set();
for (const m of contribution.matchAll(/id:"([^"]+)"[\s\S]*?\["\.\.\/([^"]+)"\]/g)) {
  (KEEP_LANGUAGES.has(m[1]) ? keepChunks : dropChunks).add(m[2]);
}

const isExcluded = (rel) => {
  const path = rel.split(sep).join("/");
  const name = path.split("/").pop();

  // Servicios de lenguaje de TypeScript, JSON, CSS y HTML (solo se usan si hay esos lenguajes)
  if (path.startsWith("language/")) return true;
  // Los workers de css/html/json/ts se cargan al arrancar el editor (aunque no se usen): se mantienen.
  if (/^(css|html|json|ts)Mode-/.test(name) || /^lspLanguageFeatures-/.test(name)) return true;

  // Fragmentos de lenguajes no usados
  if (!path.includes("/") && name.endsWith(".js") && dropChunks.has(name.slice(0, -3)) && !keepChunks.has(name.slice(0, -3)))
    return true;

  // Localizaciones de la interfaz de Monaco: solo español
  if (path.startsWith("nls/lang/") && name !== "es.js" && name !== "es.d.ts") return true;

  return false;
};

function copyTree(from, to) {
  for (const entry of readdirSync(from)) {
    const fromPath = join(from, entry);
    const toPath = join(to, entry);
    const rel = relative(src, fromPath);
    if (isExcluded(rel)) continue;

    if (statSync(fromPath).isDirectory()) {
      mkdirSync(toPath, { recursive: true });
      copyTree(fromPath, toPath);
    } else {
      copyFileSync(fromPath, toPath);
    }
  }
}

rmSync(dst, { recursive: true, force: true });
mkdirSync(dst, { recursive: true });
copyTree(src, dst);
console.log(`Monaco copiado a ${dst} (idiomas: ${[...KEEP_LANGUAGES].join(", ")})`);
