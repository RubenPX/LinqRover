// Copia Monaco Editor a wwwroot para que la app funcione sin conexión.
import { cpSync, rmSync, existsSync } from "node:fs";

const src = "node_modules/monaco-editor/min/vs";
const dst = "wwwroot/lib/monaco/vs";

if (!existsSync(src)) {
  console.error("Falta monaco-editor. Ejecuta `npm install` primero.");
  process.exit(1);
}
rmSync(dst, { recursive: true, force: true });
cpSync(src, dst, { recursive: true });
console.log("Monaco copiado a " + dst);
