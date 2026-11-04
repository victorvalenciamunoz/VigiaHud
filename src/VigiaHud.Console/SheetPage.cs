using System.Globalization;
using System.Net;
using System.Text;
using VigiaHud.Core.Recording;

namespace VigiaHud.Console;

/// <summary>
/// The page <c>hoja</c> writes: one self-contained HTML file (the photos inside as data, nothing fetched from the
/// network), in the family's dark palette (DECISIONS 005). Tapping a photo starts a stretch with somebody in, tapping
/// another ends it; the stretches become the lines of the labels file, ready to copy. Photos come as the sensor gives
/// them, so they are turned for a phone standing upright, with a button for any other way it stood.
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
            body { --w: 160px; --h: 120px; --turn: 0deg; }
            body.side { --w: 120px; --h: 160px; }
            figure { margin: 0; cursor: pointer; }
            .frame { position: relative; width: var(--w); height: var(--h); overflow: hidden; border: 2px solid transparent; border-radius: 4px; }
            .frame img { position: absolute; left: 50%; top: 50%; width: 160px; height: 120px; transform: translate(-50%, -50%) rotate(var(--turn)); }
            figcaption { font: 12px ui-monospace, Consolas, monospace; color: var(--dim); padding: 2px 0; }
            figure.person .frame { border-color: var(--marker); }
            figure.person figcaption { color: var(--marker); }
            figure.pending .frame { border-color: var(--guide); }
            </style>
            </head>
            <body>
            <header>
              <h1>{{name}}</h1>
              <div class="dim">Empieza el {{started}} (hora local) · {{photos.Count}} fotos · {{spacing}}</div>
              <div class="row">
                <span id="hint">Toca la primera foto en la que se ve a alguien y luego la última.</span>
                <button id="turn" type="button">Girar</button>
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
            let intervals = [{{existing}}];
            let pending = null;
            let turn = {{header.SensorOrientation}};
            const two = n => String(n).padStart(2, "0");
            const fmt = t => t >= 3600 ? `${Math.floor(t / 3600)}:${two(Math.floor(t / 60) % 60)}:${two(t % 60)}` : `${two(Math.floor(t / 60))}:${two(t % 60)}`;
            const figures = [...document.querySelectorAll("figure")];
            function update() {
              intervals.sort((a, b) => a[0] - b[0]);
              for (const f of figures) {
                const t = Number(f.dataset.t);
                f.classList.toggle("person", intervals.some(([s, e]) => t >= s && t <= e));
                f.classList.toggle("pending", t === pending);
              }
              const chips = document.getElementById("chips");
              chips.replaceChildren(...intervals.map((iv, i) => {
                const chip = document.createElement("span");
                chip.className = "chip";
                chip.textContent = `persona ${fmt(iv[0])}-${fmt(iv[1])}`;
                const remove = document.createElement("button");
                remove.type = "button";
                remove.textContent = "✕";
                remove.title = "Quitar este tramo";
                remove.onclick = () => { intervals.splice(i, 1); update(); };
                chip.append(remove);
                return chip;
              }));
              document.getElementById("labels").value =
                [`# ${name}`, ...(intervals.length ? intervals.map(([s, e]) => `persona ${fmt(s)}-${fmt(e)}`) : ["# sin nadie"])].join("\n") + "\n";
              document.getElementById("hint").textContent = pending === null
                ? "Toca la primera foto en la que se ve a alguien y luego la última."
                : `Empieza en ${fmt(pending)}: toca la última foto con alguien (o la misma).`;
            }
            for (const f of figures) {
              f.onclick = () => {
                const t = Number(f.dataset.t);
                if (pending === null) { pending = t; }
                else { intervals.push([Math.min(pending, t), Math.max(pending, t)]); pending = null; }
                update();
              };
            }
            function applyTurn() {
              document.body.style.setProperty("--turn", `${turn}deg`);
              document.body.classList.toggle("side", turn % 180 !== 0);
            }
            document.getElementById("turn").onclick = () => { turn = (turn + 90) % 360; applyTurn(); };
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
