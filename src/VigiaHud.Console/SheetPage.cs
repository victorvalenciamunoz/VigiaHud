using System.Globalization;
using System.Net;
using System.Text;
using VigiaHud.Core.Recording;

namespace VigiaHud.Console;

/// <summary>
/// The page <c>hoja</c> writes: one self-contained HTML file (the photos inside as data, nothing fetched from the
/// network), in the family's dark palette (DECISIONS 005). Each tap marks or unmarks one photo, with shift a whole run
/// from the previous tap; runs of marked photos become the lines of the labels file, ready to copy. What is amber is
/// what gets saved (the first version, a tap to start and a tap to end, was read as one tap per photo and left photos
/// out; 2026-11-04). Photos come as the sensor gives them, so they are turned for a phone standing upright, with a
/// button for any other way it stood, and another to brighten dark ones on screen.
/// </summary>
public static class SheetPage
{
    public sealed record Photo(TimeSpan Offset, byte[] Jpeg);

    public static string Build(SequenceHeader header, IReadOnlyList<Photo> photos, int spacingSeconds, SequenceLabels? labels, string labelsFileName)
    {
        var html = new StringBuilder();
        var name = WebUtility.HtmlEncode(header.Name);
        var started = header.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var spacing = spacingSeconds == 1 ? "todas las fotos, una por segundo" : $"una foto cada {spacingSeconds} s";
        html.Append($$"""
            <!doctype html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{name}} · VigíaHud</title>
            <style>
            :root { --bg: #0B1218; --panel: #13202A; --text: #E8F1F6; --dim: #8FA3B0; --guide: #4DD2FF; --marker: #FFC42E; }
            * { box-sizing: border-box; }
            body { margin: 0; background: var(--bg); color: var(--text); font: 15px/1.4 system-ui, "Open Sans", sans-serif; }
            header { position: sticky; top: 0; z-index: 1; background: var(--panel); padding: 12px 16px; border-bottom: 1px solid #22323E; }
            h1 { margin: 0 0 4px; font-size: 18px; letter-spacing: 1px; text-transform: uppercase; }
            .dim { color: var(--dim); }
            .row { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-top: 8px; }
            button { background: transparent; color: var(--guide); border: 1px solid var(--guide); border-radius: 6px;
                     padding: 6px 12px; font: inherit; letter-spacing: 1px; text-transform: uppercase; cursor: pointer; }
            .chip { border: 1px solid var(--marker); color: var(--marker); border-radius: 6px; padding: 2px 8px; }
            .chip button { border: 0; color: var(--marker); padding: 0 0 0 6px; }
            textarea { width: 100%; max-width: 520px; height: 5.5em; background: var(--bg); color: var(--text);
                       border: 1px solid #22323E; border-radius: 6px; font: 13px ui-monospace, Consolas, monospace; }
            main { display: grid; grid-template-columns: repeat(auto-fill, minmax(var(--w), 1fr)); gap: 6px; padding: 16px; }
            body { --w: 160px; --h: 120px; --turn: 0deg; --light: none; }
            body.side { --w: 120px; --h: 160px; }
            figure { margin: 0; cursor: pointer; }
            .frame { position: relative; width: var(--w); height: var(--h); overflow: hidden; border: 2px solid transparent; border-radius: 4px; }
            .frame img { position: absolute; left: 50%; top: 50%; width: 160px; height: 120px; transform: translate(-50%, -50%) rotate(var(--turn));
                         filter: var(--light); }
            figcaption { font: 12px ui-monospace, Consolas, monospace; color: var(--dim); padding: 2px 0; }
            figure.person .frame { border-color: var(--marker); }
            figure.person figcaption { color: var(--marker); }
            </style>
            </head>
            <body>
            <header>
              <h1>{{name}}</h1>
              <div class="dim">Empieza el {{started}} (hora local) · {{photos.Count}} fotos · {{spacing}}</div>
              <div class="row">
                <span>Toca cada foto en la que se vea a alguien, aunque sea a medias o movido; otro toque la desmarca.
                  Con Mayúsculas pulsada se marcan todas desde el toque anterior.</span>
                <button id="turn" type="button">Girar</button>
                <button id="light" type="button" title="Solo en pantalla: la grabación no cambia">Aclarar</button>
              </div>
              <div class="row" id="chips"></div>
              <div class="row">
                <textarea id="labels" readonly></textarea>
                <button id="copy" type="button">Copiar</button>
              </div>
              <div class="dim">Pega el texto entero en <code>{{WebUtility.HtmlEncode(labelsFileName)}}</code>, junto a la grabación: sustituye lo que tuviera.</div>
            </header>
            <main id="photos">
            """);

        foreach (var photo in photos)
        {
            var seconds = (long)photo.Offset.TotalSeconds;
            html.Append(CultureInfo.InvariantCulture, $"<figure data-t=\"{seconds}\"><div class=\"frame\"><img alt=\"\" src=\"data:image/jpeg;base64,")
                .Append(Convert.ToBase64String(photo.Jpeg))
                .Append(CultureInfo.InvariantCulture, $"\"></div><figcaption>{SequenceLabels.Format(TimeSpan.FromSeconds(seconds))}</figcaption></figure>\n");
        }

        var existing = string.Join(",", (labels?.People ?? []).Select(p =>
            string.Create(CultureInfo.InvariantCulture, $"[{(long)p.Start.TotalSeconds},{(long)p.End.TotalSeconds}]")));
        html.Append($$"""
            </main>
            <script>
            const name = {{System.Text.Json.JsonSerializer.Serialize(header.Name)}};
            const existing = [{{existing}}];
            let turn = {{header.SensorOrientation}};
            const two = n => String(n).padStart(2, "0");
            const fmt = t => t >= 3600 ? `${Math.floor(t / 3600)}:${two(Math.floor(t / 60) % 60)}:${two(t % 60)}` : `${two(Math.floor(t / 60))}:${two(t % 60)}`;
            const figures = [...document.querySelectorAll("figure")];
            const times = figures.map(f => Number(f.dataset.t));
            // What this sheet does not show of the stretches already labelled (before or after its window, or between
            // its photos when it skips some) is kept as it is; only the photos on the sheet can be changed.
            const onSheet = new Set(times);
            const outside = [];
            for (const [s, e] of existing) {
              let start = null;
              for (let t = s; t <= e + 1; t++) {
                const hidden = t <= e && !onSheet.has(t);
                if (hidden && start === null) start = t;
                if (!hidden && start !== null) { outside.push([start, t - 1]); start = null; }
              }
            }
            const marked = new Set(times.filter(t => existing.some(([s, e]) => t >= s && t <= e)));
            // Stretches that touch or overlap are one.
            function merge(list) {
              const result = [];
              for (const [s, e] of [...list].sort((a, b) => a[0] - b[0])) {
                const prev = result[result.length - 1];
                if (prev && s <= prev[1] + 1) prev[1] = Math.max(prev[1], e);
                else result.push([s, e]);
              }
              return result;
            }
            let anchor = null;
            // Runs of marked photos, next to each other on the sheet, are the stretches.
            function runs() {
              const result = [];
              let start = null, end = null;
              for (const t of times) {
                if (marked.has(t)) { if (start === null) start = t; end = t; }
                else if (start !== null) { result.push([start, end]); start = null; }
              }
              if (start !== null) result.push([start, end]);
              return result;
            }
            function chip(text, onRemove) {
              const span = document.createElement("span");
              span.className = "chip";
              span.textContent = text;
              if (onRemove) {
                const remove = document.createElement("button");
                remove.type = "button";
                remove.textContent = "✕";
                remove.title = "Desmarcar este tramo";
                remove.onclick = onRemove;
                span.append(remove);
              }
              return span;
            }
            function update() {
              for (const f of figures) f.classList.toggle("person", marked.has(Number(f.dataset.t)));
              const shown = runs();
              document.getElementById("chips").replaceChildren(
                ...outside.map(([s, e]) => chip(`persona ${fmt(s)}-${fmt(e)} (fuera de esta hoja)`, null)),
                ...shown.map(([s, e]) => chip(`persona ${fmt(s)}-${fmt(e)}`, () => {
                  for (const t of times) if (t >= s && t <= e) marked.delete(t);
                  update();
                })));
              const all = merge([...outside, ...shown]);
              document.getElementById("labels").value =
                [`# ${name}`, ...(all.length ? all.map(([s, e]) => `persona ${fmt(s)}-${fmt(e)}`) : ["# sin nadie"])].join("\n") + "\n";
            }
            for (const f of figures) {
              f.onclick = event => {
                const t = Number(f.dataset.t);
                if (event.shiftKey && anchor !== null) {
                  const [a, b] = [times.indexOf(anchor), times.indexOf(t)].sort((x, y) => x - y);
                  for (let i = a; i <= b; i++) marked.add(times[i]);
                } else if (marked.has(t)) {
                  marked.delete(t);
                } else {
                  marked.add(t);
                }
                anchor = t;
                update();
              };
            }
            function applyTurn() {
              document.body.style.setProperty("--turn", `${turn}deg`);
              document.body.classList.toggle("side", turn % 180 !== 0);
            }
            document.getElementById("turn").onclick = () => { turn = (turn + 90) % 360; applyTurn(); };
            // Night photos average 20 of 255 (persona-poca-luz, 2026-11-07): brighter on screen only, to see who is there.
            const lights = [1, 3, 6];
            let light = 0;
            document.getElementById("light").onclick = () => {
              light = (light + 1) % lights.length;
              const k = lights[light];
              document.body.style.setProperty("--light", k === 1 ? "none" : `brightness(${k}) contrast(1.2)`);
              document.getElementById("light").textContent = k === 1 ? "Aclarar" : `Aclarar ×${k}`;
            };
            document.getElementById("copy").onclick = async () => {
              const box = document.getElementById("labels");
              try { await navigator.clipboard.writeText(box.value); }
              catch { box.select(); document.execCommand("copy"); }
              document.getElementById("copy").textContent = "Copiado";
              setTimeout(() => document.getElementById("copy").textContent = "Copiar", 1500);
            };
            applyTurn();
            update();
            </script>
            </body>
            </html>
            """);
        return html.ToString();
    }
}
