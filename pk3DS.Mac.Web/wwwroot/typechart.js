const $ = id => document.getElementById(id);
let game, table, original;
const values = [0, 2, 4, 8];
async function post(url, body = {}) { const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }); const data = await response.json(); if (!response.ok) throw Error(data.error || data.detail || 'Operación fallida'); return data; }
function changed() { return !!table && JSON.stringify(table) !== JSON.stringify(original); }
function ui() { $('summary').textContent = changed() ? 'Tabla modificada' : 'Sin cambios para exportar'; $('export').disabled = !game?.titleId || !changed(); }
function valueLabel(value) { return value === 0 ? '0 · Inmune' : value === 2 ? '2 · Poco eficaz' : value === 4 ? '4 · Normal' : '8 · Muy eficaz'; }
function render() {
  const matrix = $('chart'); matrix.replaceChildren();
  const head = document.createElement('tr'); head.append(document.createElement('th'));
  table.types.forEach(type => { const cell = document.createElement('th'); cell.textContent = type.name; cell.title = `Defensor: ${type.name}`; head.append(cell); });
  matrix.append(head);
  for (let row = 0; row < table.typeCount; row++) {
    const line = document.createElement('tr'); const label = document.createElement('th'); label.textContent = table.types[row]?.name ?? `Tipo ${row}`; label.title = `Atacante: ${label.textContent}`; line.append(label);
    for (let column = 0; column < table.typeCount; column++) {
      const cell = document.createElement('td'); const select = document.createElement('select'); const index = row * table.typeCount + column; select.dataset.index = index;
      values.forEach(value => { const option = new Option(valueLabel(value), value); option.selected = value === table.chart[index]; select.add(option); });
      line.append(cell); cell.append(select);
    }
    matrix.append(line);
  }
  $('warning').textContent = table.warning; ui();
}
async function load() { try { game = await post('/api/workspace/inspect', { workspacePath: $('workspace').value }); $('open').disabled = false; $('editor').classList.remove('is-disabled'); $('status').textContent = `${game.gameVersion} cargado.`; } catch (error) { $('status').textContent = error.message; } ui(); }
async function open() { try { table = await post('/api/editors/typechart/table', { workspacePath: $('workspace').value, language: 1 }); original = structuredClone(table); render(); } catch (error) { $('result').textContent = error.message; } }
$('browse').onclick = async () => { try { $('workspace').value = (await post('/api/workspace/pick')).path; await load(); } catch (error) { $('status').textContent = error.message; } }; $('load').onclick = load; $('open').onclick = open;
$('chart').onchange = event => { const index = event.target.dataset.index; if (index === undefined) return; table.chart[+index] = +event.target.value; ui(); };
$('export').onclick = async () => { try { const output = await post('/api/workspace/pick-output'); const result = await post('/api/editors/typechart/export', { workspacePath: $('workspace').value, outputDirectory: output.path, titleId: game.titleId, chart: table.chart, language: 1 }); original = structuredClone(table); $('result').textContent = `Listo. ZIP: ${result.zipPath}`; ui(); } catch (error) { $('result').textContent = error.message; } };
ui();
